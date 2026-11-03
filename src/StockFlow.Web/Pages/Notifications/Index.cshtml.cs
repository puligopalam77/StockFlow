using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using StockFlow.Application.DTOs.Notifications;
using StockFlow.Application.Interfaces;

namespace StockFlow.Web.Pages.Notifications;

public sealed class IndexModel(INotificationRepository repository) : PageModel
{
    [BindProperty(SupportsGet=true)] public int PageNumber { get; set; }=1;
    [BindProperty(SupportsGet=true)] public int PageSize { get; set; }=10;
    public bool Ready { get; private set; }
    public NotificationFeed Feed { get; private set; } = new(0,0,[]);
    public async Task OnGetAsync() {
        PageNumber=Math.Clamp(PageNumber,1,10000);if(!new[]{10,25,50,100}.Contains(PageSize))PageSize=10;
        Ready=await repository.ReadyAsync();if(!Ready)return;
        Feed=await repository.FeedPageAsync(PageNumber,PageSize);
        var last=(int)Math.Max(1,Math.Min(10000,(Feed.Total+PageSize-1)/PageSize));
        if(PageNumber>last){PageNumber=last;Feed=await repository.FeedPageAsync(PageNumber,PageSize);}
    }
    public async Task<IActionResult> OnPostReadAsync(long? id)
    {
        if (id is <=0) return BadRequest();
        if (!await repository.ReadyAsync()) return StatusCode(503);
        await repository.MarkReadAsync(id);return RedirectToPage(new { PageNumber,PageSize });
    }
}
