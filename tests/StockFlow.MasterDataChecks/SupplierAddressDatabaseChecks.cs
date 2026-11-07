using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StockFlow.Application.DTOs.Suppliers;
using StockFlow.Application.Services;
using StockFlow.Infrastructure.Data;
using StockFlow.Infrastructure.Repositories;

namespace StockFlow.MasterDataChecks;

internal static class SupplierAddressDatabaseChecks
{
    public static async Task Run()
    {
        var connectionString = Environment.GetEnvironmentVariable("STOCKFLOW_SUPPLIER_ADDRESS_TEST_DB")
            ?? throw new Exception("Set STOCKFLOW_SUPPLIER_ADDRESS_TEST_DB to a disposable local test database.");
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        if (settings.Host != "127.0.0.1" || settings.Database != "supplier_address_checks" || settings.Port == 5432)
            throw new Exception("Only the disposable local supplier_address_checks database on a nondefault port is allowed.");
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        if (await connection.ExecuteScalarAsync<bool>("SELECT to_regclass('public.suppliers') IS NOT NULL"))
            throw new Exception("Test database must be empty; no existing records will be changed.");
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "StockFlow.sln"))) root = root.Parent;
        await connection.ExecuteAsync("""
            CREATE TABLE suppliers(supplier_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
              tenant_id integer NOT NULL DEFAULT 1,supplier_name varchar(150) NOT NULL,contact_person varchar(100),
              mobile_number varchar(20),email varchar(150),address varchar(500),gst_number varchar(15),
              is_active boolean NOT NULL DEFAULT true,created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz);
            CREATE TABLE purchases(purchase_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
              tenant_id integer NOT NULL DEFAULT 1,supplier_id integer NOT NULL REFERENCES suppliers(supplier_id),status text NOT NULL DEFAULT 'DRAFT');
            CREATE TABLE audit_logs(tenant_id integer,user_id integer,module_name text,action_name text,table_name text,record_id integer,old_values jsonb,new_values jsonb);
            CREATE FUNCTION sf_actor() RETURNS integer LANGUAGE sql AS 'SELECT 1';
            CREATE TABLE stores(management_version bigint);
            """);
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root!.FullName,"database/migrations/023_supplier_registration.sql")));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> { ["ConnectionStrings:DefaultConnection"] = connectionString }).Build();
        var repository = new SupplierRepository(new DbConnectionFactory(config));
        var service = new SupplierService(repository);
        var legacyId = await service.CreateAsync(new CreateSupplierRequest { SupplierName="Legacy supplier", Address="Old building\nUnsplit historic address" });
        Check((await service.GetByIdAsync(legacyId))!.AddressLine1 is null,"Pre-migration reads and ordinary registration work");
        try { await service.CreateAsync(new CreateSupplierRequest {SupplierName="Pending address",UseStructuredAddress=true,AddressLine1="New street"});throw new Exception("Pending setup must reject unsaved structured lines"); }
        catch(System.ComponentModel.DataAnnotations.ValidationException) { Check(true,"Pending migration rejects structured lines without a partial insert"); }
        Check((await service.GetAllAsync()).Count()==1,"Pending setup does not create a duplicate or partial record");
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations/090_supplier_structured_address.sql")));
        try { await service.CreateAsync(new CreateSupplierRequest {SupplierName="Pending district",District="Bengaluru Urban"});throw new Exception("Pending district setup must reject district writes"); }
        catch(System.ComponentModel.DataAnnotations.ValidationException) { Check(true,"Pending district migration rejects writes without losing metadata"); }
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations/091_supplier_address_district.sql")));
        var legacy=await service.GetByIdAsync(legacyId);
        Check(legacy!.Address=="Old building\nUnsplit historic address" && legacy.AddressLine1 is null && !legacy.UseStructuredAddress,"Migration preserves legacy address without inferring components");
        foreach(var (line1,line2) in new (string?,string?)[]{("12 Market Street","First floor"),("Only first line",null),(null,null),(null,"Only second line")})
        {
            var id=await service.CreateAsync(new CreateSupplierRequest {SupplierName="Optional address",UseStructuredAddress=true,AddressLine1=line1,AddressLine2=line2,City="Bengaluru",State="Karnataka",District="Bengaluru Urban",PostalCode="560001"});
            var saved=(await service.GetByIdAsync(id))!;
            Check(saved.AddressLine1==line1 && saved.AddressLine2==line2 && saved.UseStructuredAddress,"Real repository persists and reopens optional lines");
            Check(saved.City=="Bengaluru" && saved.State=="Karnataka" && saved.PostalCode=="560001","City, state and PIN persist");
            Check(saved.District=="Bengaluru Urban","Explicitly selected SchoolERP-style district persists and reopens");
            var purchaseId=await connection.ExecuteScalarAsync<int>("INSERT INTO purchases(supplier_id,status) VALUES(@id,'COMPLETED') RETURNING purchase_id",new{id});
            await service.UpdateAsync(id,new UpdateSupplierRequest {SupplierName="Edited address",UseStructuredAddress=true,AddressLine1="Changed street",IsActive=false,PaymentTermsDays=30});
            var edited=(await service.GetByIdAsync(id))!;
            Check(edited.SupplierCode==saved.SupplierCode && edited.Address=="Changed street" && !edited.IsActive,"Address edit retains supplier code and inactive status");
            Check(await connection.ExecuteScalarAsync<string?>("SELECT supplier_address_snapshot FROM purchases WHERE purchase_id=@purchaseId",new{purchaseId})==await connection.ExecuteScalarAsync<string?>("SELECT concat_ws(', ',@address::text,@city::text,@state::text,@pin::text,'IN')",new{address=saved.Address,city=saved.City,state=saved.State,pin=saved.PostalCode}),"Completed purchase keeps its original address snapshot");
            Check(await connection.ExecuteScalarAsync<int?>("SELECT payment_terms_days FROM purchases WHERE purchase_id=@purchaseId",new{purchaseId}) is null,"Changing supplier terms does not rewrite completed purchase terms");
            await connection.ExecuteAsync("UPDATE suppliers SET address='Older application explicit address' WHERE supplier_id=@id",new{id});
            edited=(await service.GetByIdAsync(id))!;
            Check(!edited.UseStructuredAddress && edited.AddressLine1 is null && edited.Address=="Older application explicit address","Older application address edits invalidate stale structured components safely");
        }
        var ids=await Task.WhenAll(Enumerable.Range(0,24).Select(i=>service.CreateAsync(new CreateSupplierRequest {SupplierName="Concurrent supplier "+i})));
        var codes=await connection.QueryAsync<string>("SELECT supplier_code FROM suppliers WHERE supplier_id=ANY(@ids)",new{ids});
        Check(codes.Distinct().Count()==24 && codes.All(code=>code.StartsWith("SUP")),"24 concurrent creates use unique codes from the unchanged migration 023 generator");
        var matches=(await repository.GetAllAsync(term:"Older application explicit address")).ToList();
        Check(matches.Count==4 && matches.All(s=>s.Address=="Older application explicit address"),"Repository search retains compatible combined address projection");
        await connection.ExecuteAsync("""
            CREATE TABLE customers(customer_id integer GENERATED ALWAYS AS IDENTITY PRIMARY KEY,tenant_id integer NOT NULL DEFAULT 1,
             customer_name varchar(150) NOT NULL,mobile_number varchar(20),email varchar(150),address varchar(1000),gst_number varchar(15),
             is_active boolean NOT NULL DEFAULT true,created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz);
            """);
        var customers=new CustomerService(new CustomerRepository(new DbConnectionFactory(config)));
        var legacyCustomer=await customers.CreateAsync(new StockFlow.Application.DTOs.Customers.CreateCustomerRequest {CustomerName="Legacy customer",Address="Original customer address, unsplit"});
        Check((await customers.GetByIdAsync(legacyCustomer))!.AddressDetails is null,"Customer legacy reads work before metadata migration");
        var details=new StockFlow.Application.Common.AddressComponents{AddressLine1="20 Retail Street",AddressLine2="Floor 2",City="Bengaluru",District="Bengaluru Urban",State="Karnataka",PostalCode="560001"};
        try{await customers.CreateAsync(new StockFlow.Application.DTOs.Customers.CreateCustomerRequest{CustomerName="Pending customer address",AddressDetails=details});throw new Exception("Missing customer metadata migration must reject structured writes");}
        catch(System.ComponentModel.DataAnnotations.ValidationException){Check(true,"Missing customer migration cannot silently discard address components");}
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root.FullName,"database/migrations/092_customer_address_components.sql")));
        var customerId=await customers.CreateAsync(new StockFlow.Application.DTOs.Customers.CreateCustomerRequest{CustomerName="Structured customer",MobileNumber="9876543210",AddressDetails=details},true);
        var customer=(await customers.GetByIdAsync(customerId))!;
        Check(customer.AddressDetails?.District=="Bengaluru Urban" && customer.AddressDetails.AddressLine2=="Floor 2" && customer.Address!.Contains("20 Retail Street"),"Customer components and compatible printable address persist through real quick-add repository");
        await customers.UpdateAsync(customerId,new StockFlow.Application.DTOs.Customers.UpdateCustomerRequest{CustomerName="Older client customer edit",Address=customer.Address});
        Check((await customers.GetByIdAsync(customerId))!.AddressDetails?.District=="Bengaluru Urban","Older customer client retains structured metadata on ordinary edits");
        await customers.UpdateAsync(customerId,new StockFlow.Application.DTOs.Customers.UpdateCustomerRequest{CustomerName="Explicit empty address",UseStructuredAddress=true});
        customer=(await customers.GetByIdAsync(customerId))!;
        Check(customer.Address is null && customer.AddressDetails is null,"Explicitly clearing an optional customer address clears both representations");
        await customers.UpdateAsync(customerId,new StockFlow.Application.DTOs.Customers.UpdateCustomerRequest{CustomerName="Restored structured customer",AddressDetails=details});
        await connection.ExecuteAsync("UPDATE customers SET address='Older application combined text' WHERE customer_id=@customerId",new{customerId});
        customer=(await customers.GetByIdAsync(customerId))!;
        Check(customer.Address=="Older application combined text" && customer.AddressDetails is null,"Older customer writers invalidate stale components safely");
        Check((await customers.GetByIdAsync(legacyCustomer))!.Address=="Original customer address, unsplit","Customer migration does not rewrite legacy text");
        var directoryId = await service.CreateAsync(new CreateSupplierRequest { SupplierName="Directory %_ Supplier",UseStructuredAddress=true,AddressLine1="Retained directory street",State="Karnataka",District="Bengaluru Urban",GstNumber="29ABCDE1234F1Z5",PaymentTermsDays=45,Notes="Retain directory notes" });
        var directoryBefore = (await service.GetByIdAsync(directoryId))!;
        var directoryPurchase = await connection.ExecuteScalarAsync<int>("INSERT INTO purchases(supplier_id,status) VALUES(@directoryId,'COMPLETED') RETURNING purchase_id",new{directoryId});
        var snapshotBefore = await connection.ExecuteScalarAsync<string>("SELECT row_to_json(p)::text FROM purchases p WHERE purchase_id=@directoryPurchase",new{directoryPurchase});
        var pageOne = await repository.GetPageAsync(null,null,0,10);
        var pageTwo = await repository.GetPageAsync(null,null,10,10);
        Check(pageOne.Total == (await service.GetAllAsync()).Count() && pageOne.Items.Count==10 && !pageOne.Items.Select(s=>s.SupplierId).Intersect(pageTwo.Items.Select(s=>s.SupplierId)).Any(),"Real SQL directory has exact counts and distinct stable pages");
        Check((await repository.GetPageAsync("%_",null,0,10)).Total==1,"Directory SQL treats wildcard characters as literal search text");
        Check((await repository.GetPageAsync("29abcde1234f1z5",null,0,10)).Items.Any(s=>s.SupplierId==directoryId),"Real SQL directory searches GSTIN case-insensitively");
        Check(await service.SetActiveAsync(directoryId,false),"Real status endpoint repository deactivates supplier");
        var directoryAfter=(await service.GetByIdAsync(directoryId))!;
        Check(!directoryAfter.IsActive && directoryAfter.SupplierCode==directoryBefore.SupplierCode && directoryAfter.AddressLine1==directoryBefore.AddressLine1 && directoryAfter.District==directoryBefore.District && directoryAfter.Notes==directoryBefore.Notes && directoryAfter.PaymentTermsDays==45,"Status-only SQL retains identity, metadata and purchasing preferences");
        Check((await repository.GetPageAsync("%_",false,0,10)).Total==1 && (await repository.GetPageAsync("%_",true,0,10)).Total==0,"Real SQL inactive and active filters agree with totals");
        Check(await connection.ExecuteScalarAsync<string>("SELECT row_to_json(p)::text FROM purchases p WHERE purchase_id=@directoryPurchase",new{directoryPurchase})==snapshotBefore,"Supplier status does not alter completed purchase snapshots");
        await service.SetActiveAsync(directoryId,true);
        var secretId=await connection.ExecuteScalarAsync<int>("INSERT INTO suppliers(tenant_id,supplier_name) VALUES(2,'Other tenant directory secret') RETURNING supplier_id");
        await connection.ExecuteAsync("""
            CREATE ROLE stockflow_directory_checks NOLOGIN NOSUPERUSER NOBYPASSRLS;
            GRANT USAGE ON SCHEMA public TO stockflow_directory_checks;
            GRANT SELECT,UPDATE ON public.suppliers TO stockflow_directory_checks;
            GRANT INSERT ON public.audit_logs TO stockflow_directory_checks;
            ALTER TABLE public.suppliers ENABLE ROW LEVEL SECURITY;
            ALTER TABLE public.suppliers FORCE ROW LEVEL SECURITY;
            CREATE POLICY directory_fixture_tenant ON public.suppliers
             USING(tenant_id=current_setting('app.tenant_id')::integer)
             WITH CHECK(tenant_id=current_setting('app.tenant_id')::integer);
            """);
        var scopedString=new NpgsqlConnectionStringBuilder(connectionString){Options="-c role=stockflow_directory_checks -c app.tenant_id=1"}.ConnectionString;
        var scopedRepo=new SupplierRepository(new DbConnectionFactory(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"ConnectionStrings:DefaultConnection",scopedString}}).Build()));
        Check((await scopedRepo.GetPageAsync("Other tenant directory secret",null,0,10)).Total==0,"Paged query and count obey a real non-bypass tenant RLS policy");
        Check(!await scopedRepo.SetActiveAsync(secretId,false) && await connection.ExecuteScalarAsync<bool>("SELECT is_active FROM suppliers WHERE supplier_id=@secretId",new{secretId}),"Forged cross-tenant status target cannot update the real SQL row");
        Console.WriteLine("Database checks use an isolated PostgreSQL cluster and actual migrations 023/090/091/092 plus the real repository. The directory tenant RLS fixture is explicit; full production shop/profile/RLS migration chains are not installed.");
    }
    private static void Check(bool ok,string message){if(!ok)throw new Exception(message);Console.WriteLine("PASS: "+message);}
}
