window.StockFlowCsv = (() => {
    'use strict';
    function parse(text) {
        if(text.length>2000000)throw new Error('Use a CSV file up to 2 MB.');
        const rows=[];let row=[],value='',quoted=false;
        text=text.replace(/^\uFEFF/,'');
        for(let i=0;i<text.length;i++){
            const c=text[i];
            if(c==='"'){
                if(quoted&&text[i+1]==='"'){value+='"';i++;}
                else if(quoted){quoted=false;if(text[i+1]&&!['\r','\n',','].includes(text[i+1]))throw new Error('Invalid CSV quoting.');}
                else if(value==='')quoted=true;else throw new Error('Quote CSV values containing commas.');
            }else if(!quoted&&(c===','||c==='\n'||c==='\r')){
                row.push(value);value='';
                if(c!==','){if(c==='\r'&&text[i+1]==='\n')i++;if(row.some(v=>v.trim()))rows.push(row);row=[];}
            }else value+=c;
        }
        if(quoted)throw new Error('Unclosed quoted CSV value.');
        row.push(value);if(row.some(v=>v.trim()))rows.push(row);
        if(rows.length<2)throw new Error('Include a header and at least one data row.');
        if(rows.length>1001)throw new Error('Import up to 1,000 CSV rows at a time.');
        const headers=rows.shift().map(v=>v.trim());
        if(headers.some(v=>!v)||new Set(headers).size!==headers.length)throw new Error('CSV headers must be unique and nonempty.');
        return rows.map((r,i)=>{if(r.length!==headers.length)throw new Error('Wrong number of columns on CSV row '+(i+2)+'.');return Object.fromEntries(headers.map((h,n)=>[h,r[n].trim()]));});
    }
    function download(name,headers,rows){
        const cell=v=>{const text=String(v??'');return '"'+(/^[=+\-@\t\r]/.test(text)?"'"+text:text).replaceAll('"','""')+'"';};
        const blob=new Blob(['\uFEFF'+[headers,...rows].map(r=>r.map(cell).join(',')).join('\r\n')],{type:'text/csv;charset=utf-8'});
        const url=URL.createObjectURL(blob),link=document.createElement('a');link.href=url;link.download=name;link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
    }
    return {parse,download};
})();
