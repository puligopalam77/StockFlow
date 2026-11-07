using System.Net.Http.Json;
using System.Text.Json;
using StockFlow.Application.Interfaces;

namespace StockFlow.MasterDataChecks;

internal static class ExpenseSettlementChecks
{
    static void Check(bool value, string name)
    {
        if (!value) throw new Exception(name);
        Console.WriteLine("PASS: " + name);
    }

    public static async Task Run(IExpenseRepository repo, HttpClient client, Guid category, Guid payee, DateTime today)
    {
        var day = today.ToString("yyyy-MM-dd");
        var id = Guid.NewGuid();
        var record = new ExpenseOperation(Guid.NewGuid(), 0, JsonSerializer.SerializeToElement(new
        {
            expense = new { categoryId = category, payeeId = payee, incurredDate = day, amount = 1200m, description = "AC repair lifecycle" },
            payment = new { paidDate = day, amount = 500m, paymentMode = "BANK_TRANSFER", reference = "", confirmed = true }
        }));
        (await client.PostAsJsonAsync($"/api/expenses/{id}/record", record)).EnsureSuccessStatusCode();
        var expense = await repo.GetAsync(id);
        Check(expense.GetProperty("paid").GetDecimal() == 500m && expense.GetProperty("payments").GetArrayLength() == 1,
            "Partial bank transfer with an optional empty reference records 500 of 1200");

        ExpenseOperation Payment(decimal amount, string method, long version) => new(Guid.NewGuid(), version,
            JsonSerializer.SerializeToElement(new { paidDate = day, amount, paymentMode = method, reference = "" }));
        var version = expense.GetProperty("version").GetInt64();
        foreach (var amount in new[] { 0m, -1m, 700.01m, 0.001m })
        {
            var rejected = await client.PostAsJsonAsync($"/api/expenses/{id}/pay", Payment(amount, "BANK_TRANSFER", version));
            Check((int)rejected.StatusCode == 400, "Later payment rejects zero, negative, excessive or fractional paise: " + amount);
        }
        var operation = Payment(300m, "CASH", version);
        var results = await Task.WhenAll(client.PostAsJsonAsync($"/api/expenses/{id}/pay", operation), client.PostAsJsonAsync($"/api/expenses/{id}/pay", operation));
        expense = await repo.GetAsync(id);
        Check(results.All(r => r.IsSuccessStatusCode) && expense.GetProperty("paid").GetDecimal() == 800m && expense.GetProperty("payments").GetArrayLength() == 2,
            "Concurrent later-payment retries append only one transaction and preserve initial history");
        var changedRetry = operation with { Body = Payment(301m, "CASH", version).Body };
        Check((int)(await client.PostAsJsonAsync($"/api/expenses/{id}/pay", changedRetry)).StatusCode == 400,
            "A changed payment retry cannot alter the original transaction");
        (await client.PostAsJsonAsync($"/api/expenses/{id}/pay", Payment(400m, "BANK_TRANSFER", expense.GetProperty("version").GetInt64()))).EnsureSuccessStatusCode();
        expense = await repo.GetAsync(id);
        Check(expense.GetProperty("paid").GetDecimal() == 1200m && expense.GetProperty("payments").GetArrayLength() == 3 &&
              expense.GetProperty("payments").EnumerateArray().Select(p => p.GetProperty("amount").GetDecimal()).Order().SequenceEqual(new[] { 300m, 400m, 500m }),
            "Final remaining bank payment settles the expense without overwriting any payment history");
        Check((int)(await client.PostAsJsonAsync($"/api/expenses/{id}/pay", Payment(1m, "UPI", expense.GetProperty("version").GetInt64()))).StatusCode == 400,
            "A fully settled expense cannot receive an overpayment");

        var draft = Guid.NewGuid();
        await repo.WriteAsync(draft, "SAVE", new(Guid.NewGuid(), 0, record.Body.GetProperty("expense")));
        var pdf = "%PDF-1.4 synthetic expense evidence"u8.ToArray();
        var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10, 1, 2, 3 };
        var jpeg = new byte[] { 255, 216, 255, 1, 2, 3 };
        async Task<HttpResponseMessage> Upload(Guid expenseId, Guid op, long expected, string name, byte[] bytes, string mime = "application/octet-stream")
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(op.ToString()), "operationId");
            form.Add(new StringContent(expected.ToString()), "expectedVersion");
            var file = new ByteArrayContent(bytes);
            file.Headers.ContentType = new(mime);
            form.Add(file, "file", name);
            return await client.PostAsync($"/api/expenses/{expenseId}/attachments", form);
        }
        var attach = Guid.NewGuid();
        (await Upload(draft, attach, 1, "draft.pdf", pdf)).EnsureSuccessStatusCode();
        (await Upload(draft, attach, 1, "draft.pdf", pdf)).EnsureSuccessStatusCode();
        var savedDraft = await repo.GetAsync(draft);
        Check(savedDraft.GetProperty("status").GetString() == "DRAFT" && savedDraft.GetProperty("paid").GetDecimal() == 0m &&
              savedDraft.GetProperty("payments").GetArrayLength() == 0 && savedDraft.GetProperty("attachments").GetArrayLength() == 1,
            "Draft receipts use the existing attachment endpoint without recording a payment; exact retries add no duplicate");
        foreach (var (name, bytes) in new[] { ("photo.png", png), ("photo.jpg", jpeg), ("photo.jpeg", jpeg) })
        {
            (await Upload(id, Guid.NewGuid(), expense.GetProperty("version").GetInt64(), name, bytes)).EnsureSuccessStatusCode();
            expense = await repo.GetAsync(id);
        }
        Check(expense.GetProperty("attachments").GetArrayLength() == 3, "Posted expenses accept PNG, JPG and JPEG through existing authenticated storage");
        var attachmentId = savedDraft.GetProperty("attachments")[0].GetProperty("id").GetGuid();
        var download = await client.GetAsync($"/api/expenses/{draft}/attachments/{attachmentId}");
        Check(download.IsSuccessStatusCode && download.Content.Headers.ContentType?.MediaType == "application/pdf" &&
              (await download.Content.ReadAsByteArrayAsync()).SequenceEqual(pdf), "Receipt download preserves bytes and uses the detected content type");
        foreach (var bytes in new[] { Array.Empty<byte>(), "pretend PDF"u8.ToArray(), new byte[5 * 1024 * 1024 + 1] })
            Check((int)(await Upload(draft, Guid.NewGuid(), savedDraft.GetProperty("version").GetInt64(), "forged.pdf", bytes, "application/pdf")).StatusCode == 400,
                "Receipt endpoint rejects empty, forged MIME/signature or oversized content: " + bytes.Length);
        Check(!(await Upload(Guid.NewGuid(), Guid.NewGuid(), 0, "orphan.pdf", pdf)).IsSuccessStatusCode,
            "Receipt upload cannot create an orphan against an unknown expense");
        Check((int)(await client.GetAsync($"/api/expenses/{id}/attachments/{attachmentId}")).StatusCode == 404,
            "Receipt downloads are tied to their authorized expense ID");
        client.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");
        Check((int)(await Upload(draft, Guid.NewGuid(), savedDraft.GetProperty("version").GetInt64(), "csrf.pdf", pdf)).StatusCode == 400,
            "Receipt uploads still require antiforgery protection");
        var token = await client.GetFromJsonAsync<JsonElement>("/api/antiforgery");
        client.DefaultRequestHeaders.Add("X-CSRF-TOKEN", token.GetProperty("requestToken").GetString());
    }
}
