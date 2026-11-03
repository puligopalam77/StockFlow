// Uses rendered Razor fixtures and mocked API responses; never contacts the application database.
const fs = require('fs'), path = require('path'), assert = require('assert');
const root = path.resolve(__dirname, '../..');
const { chromium } = require(path.join(__dirname, 'bin/Debug/net10.0/.playwright/package'));
const fixtures = path.join(root, 'tests/bin/phase12-ui-fixtures');
const staticRoot = path.join(root, 'src/StockFlow.Web/wwwroot');
const base = 'http://localhost:51999';
const transitions = {
    RECEIVED: ['INSPECTION','CANCELLED'], INSPECTION: ['REPAIRING','WAITING_FOR_PART','READY','CANCELLED'],
    REPAIRING: ['WAITING_FOR_PART','READY'], WAITING_FOR_PART: ['REPAIRING','READY'], READY: ['DELIVERED'], DELIVERED: []
};
let ticket = {
    serviceRequestId:1, serviceNumber:'SRV-2026-000001', customerId:1, customerName:'Customer <script>bad()</script>',
    customerMobile:'9876543210', productId:1, productName:'Battery', productUnitId:1, serialNumber:'BAT001',
    warrantyId:1, warrantyStatus:'EXPIRED', isUnderWarranty:false, underWarrantyAtReceipt:false,
    status:'RECEIVED', version:1, complaint:'', diagnosis:'', repairDetails:'', serviceCharge:0, partsTotal:0,
    totalCharge:0, receivedDate:'2026-09-29T10:00:00', expectedDeliveryDate:null, deliveredDate:null,
    assignedTo:3, assignedToName:'Technician', notes:'', allowedTransitions:transitions.RECEIVED
};
let parts = [], history = [], created, issued = 0;
(async () => {
    const browser = await chromium.launch({channel:'msedge',headless:true});
    try {
        const page = await browser.newPage(), errors = [];
        page.on('pageerror', e => errors.push(e.message));
        await page.route('**/*', async route => {
            const req=route.request(), url=new URL(req.url()), p=url.pathname;
            const json=(data,status=200)=>route.fulfill({status,contentType:'application/json',body:JSON.stringify(data)});
            if(p.startsWith('/api/')) {
                if(p==='/api/notifications') return json({unread:0,items:[]});
                if(p==='/api/screenlanguages') return json([]);
                if(p==='/api/branches/context') return json({locationId:1,storeId:1,canReviewStores:true,locations:[{locationId:1,storeId:1,storeName:'Main store',locationName:'Main stock'}]});
                if(p==='/api/operations/service/1') return json({documentTotal:ticket.totalCharge,paidTotal:0,outstanding:ticket.totalCharge,payments:[]});
                if(p==='/api/scan') return json({productId:1,productUnitId:1,isSerialTracked:true,serialNumber:'BAT001'});
                if(p==='/api/service/units/1') return json({productId:1,variantId:1,productUnitId:1,productName:'Battery',serialNumber:'BAT001',unitStatus:'SOLD',
                    saleStatus:'COMPLETED',saleId:1,invoiceNumber:'INV-1',customerId:1,customerName:ticket.customerName,customerMobile:ticket.customerMobile,
                    warranty:{warrantyId:1,warrantyStatus:'EXPIRED',isUnderWarranty:false}});
                if(p==='/api/service/technicians') return json([{userId:3,fullName:'Technician'}]);
                if(p==='/api/products/search') return json([{productId:2,productName:'Fuse',isSerialTracked:false,sellingPrice:10}]);
                if(p==='/api/products/variants') return json([{productId:2,variantId:2,productName:'Fuse',isSerialTracked:false,sellingPrice:10}]);
                if(p==='/api/service' && req.method()==='POST') {
                    created=req.postDataJSON(); Object.assign(ticket,{complaint:created.complaint,assignedTo:created.assignedTo});
                    assert(created.requestId && req.headers()['x-csrf-token']);
                    history.push({actionName:'CREATE',newStatus:'RECEIVED',changedAt:'2026-09-29T10:00:00',changedByName:'Service'});
                    return json({serviceRequestId:1},201);
                }
                if(p==='/api/service' || p==='/api/service/pending') {
                    const status=url.searchParams.get('status'), pending=url.searchParams.get('pendingOnly')==='true';
                    return json((status && status!==ticket.status) || (pending && ticket.status==='DELIVERED') ? [] : [ticket]);
                }
                if(p==='/api/service/1' && req.method()==='GET') return json(ticket);
                if(p==='/api/service/1/parts' && req.method()==='GET') return json(parts);
                if(p==='/api/service/1/history') return json(history);
                if(p==='/api/service/1' || p==='/api/service/1/status' || p==='/api/service/1/parts') {
                    const data=req.postDataJSON();
                    assert(data.requestId && req.headers()['x-csrf-token']);
                    if(data.expectedVersion!==ticket.version) return json({message:'Reload stale ticket.'},400);
                    if(p.endsWith('/status')) {
                        assert(transitions[ticket.status].includes(data.status));
                        history.push({actionName:'STATUS',oldStatus:ticket.status,newStatus:data.status,remarks:data.remarks,changedAt:'2026-09-29T11:00:00',changedByName:'Technician'});
                        ticket.status=data.status; ticket.allowedTransitions=transitions[data.status];
                        if(data.status==='DELIVERED') ticket.deliveredDate='2026-09-29T12:00:00';
                    } else if(p.endsWith('/parts')) {
                        issued++; assert.strictEqual(data.productId,2); assert.strictEqual(data.quantity,2);
                        parts.push({productName:'Fuse',quantity:2,unitPrice:data.unitPrice,totalAmount:2*data.unitPrice,createdAt:'2026-09-29T11:10:00'});
                        ticket.partsTotal+=2*data.unitPrice;
                    } else Object.assign(ticket,data);
                    ticket.version++; ticket.totalCharge=ticket.partsTotal+ticket.serviceCharge;
                    return json(p.endsWith('/parts') ? {servicePartId:1} : {});
                }
                throw new Error('Unexpected API route: '+req.method()+' '+p);
            }
            const pages = {'/Service/Create':'Create','/Service/Details/1':'Details-1','/Service':'Index','/Service/Index':'Index','/Service/Pending':'Pending'};
            if(pages[p]) return route.fulfill({contentType:'text/html',body:fs.readFileSync(path.join(fixtures,pages[p]+'.html'),'utf8')});
            const file=path.resolve(staticRoot,'.'+p.replace(/\.[a-z0-9]{10}(?=\.(js|css)$)/,''));
            if(file.startsWith(staticRoot+path.sep) && fs.existsSync(file) && fs.statSync(file).isFile())
                return route.fulfill({path:file,contentType:p.endsWith('.js')?'text/javascript':p.endsWith('.css')?'text/css':undefined});
            return route.fulfill({status:404,body:''});
        });
        await page.goto(base+'/Service/Create');
        await page.waitForFunction(()=>!document.getElementById('create-fields').disabled);
        await page.locator('#service-scan').fill('BAT001'); await page.locator('#service-scan').press('Enter');
        await page.waitForFunction(()=>document.getElementById('scan-context').textContent.includes('Expired'));
        assert(await page.locator('#product').isDisabled()); assert(await page.locator('#customer').isDisabled());
        assert.strictEqual(await page.locator('#customer').inputValue(),'1');
        await page.locator('#complaint').fill('Not charging'); await page.locator('#technician').selectOption('3');
        await page.locator('#service-create button:not([type])').click();
        await page.waitForURL(base+'/Service/Details/1'); await page.locator('#service-edit').waitFor({state:'visible'});
        assert.strictEqual(created.productUnitId,1); assert.strictEqual(created.warrantyId,1);
        assert((await page.locator('#service-summary').innerText()).includes('<script>bad()</script>'));
        await page.locator('#diagnosis').fill('Fuse fault'); await page.locator('#repair').fill('Replace fuse'); await page.locator('#charge').fill('25');
        await page.locator('#details-form button').click();
        await page.waitForFunction(()=>document.getElementById('service-summary').textContent.includes('Fuse fault'));
        async function status(next) {
            await page.locator('#next-status').selectOption(next); await page.locator('#remarks').fill('Move to '+next);
            await page.locator('#status-form button').click();
            const captions={INSPECTION:'Being checked',REPAIRING:'Being repaired',WAITING_FOR_PART:'Waiting for a part',READY:'Ready for collection',DELIVERED:'Delivered'};
            await page.waitForFunction(value=>document.querySelector('#service-summary dd')?.textContent===value,captions[next]);
            if(next!=='DELIVERED') await page.locator('#service-edit').waitFor({state:'visible'});
        }
        await status('INSPECTION');
        await page.locator('#find-parts').click(); await page.locator('#part-product').selectOption('2');
        await page.locator('#part-quantity').fill('2'); await page.locator('#part-price').fill('10');
        await page.locator('#part-form button:not([type])').click();
        await page.waitForFunction(()=>document.getElementById('parts-results').textContent.includes('Fuse'));
        assert.strictEqual(ticket.totalCharge,45); assert.strictEqual(issued,1);
        await status('WAITING_FOR_PART'); await status('REPAIRING'); await status('READY'); await status('DELIVERED');
        assert(await page.locator('#service-edit').isHidden());
        await page.goto(base+'/Service/Pending');
        await page.waitForFunction(()=>document.getElementById('service-results').textContent.includes('No records'));
        await page.goto(base+'/Service/Index?status=DELIVERED');
        await page.waitForFunction(()=>document.getElementById('service-results').textContent.includes('SRV-2026-000001'));
        for(const width of [1440,390]) {
            await page.setViewportSize({width,height:900});
            // Let the existing shell's sidebar margin transition finish after a breakpoint change.
            await page.waitForFunction(()=>document.documentElement.scrollWidth<=innerWidth+2,null,{timeout:3000});
            const overflow = await page.evaluate(() => ({
                width:innerWidth, scroll:document.documentElement.scrollWidth,
                elements:[...document.querySelectorAll('body *')].filter(e=>e.getBoundingClientRect().right>innerWidth+2)
                    .slice(0,8).map(e=>({tag:e.tagName,cls:e.className,right:e.getBoundingClientRect().right}))
            }));
            assert(overflow.scroll<=width+2,'service list overflows viewport: '+JSON.stringify(overflow));
        }
        assert.deepStrictEqual(errors,[]);
        console.log('PASS: service browser intake, expired warranty, locked customer/product, repair charge, part issue, lifecycle, history lists, encoding and responsive layout. Mock API only.');
    } finally { await browser.close(); }
})().catch(e=>{console.error(e);process.exit(1);});
