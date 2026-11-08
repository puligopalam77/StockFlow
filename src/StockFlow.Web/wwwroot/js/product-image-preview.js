// Ordered product gallery. Pending changes are committed by the existing Product Save API.
window.ProductImageGallery = {
    bind(fileInput,urlInput){
        const node=(tag,text,cls)=>{const element=document.createElement(tag);if(text)element.textContent=text;if(cls)element.className=cls;return element;};
        const icons={Eye:'<path d="M2.062 12.348a1 1 0 0 1 0-.696 10.75 10.75 0 0 1 19.876 0 1 1 0 0 1 0 .696 10.75 10.75 0 0 1-19.876 0"/><circle cx="12" cy="12" r="3"/>',RefreshCw:'<path d="M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8M21 3v5h-5M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16M8 16H3v5"/>',Trash2:'<path d="M3 6h18M19 6l-1 14H6L5 6M9 6V3h6v3M10 10v7M14 10v7"/>',ImagePlus:'<path d="M16 5H5v14h14V9M16 3v4M14 5h4M5 16l4-4 4 4 3-3 3 3"/><circle cx="9" cy="8" r="1"/>'};
        const svg=name=>'<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">'+icons[name]+'</svg>';
        const host=node('section',null,'product-image-gallery');host.setAttribute('aria-label','Product image gallery');
        const stage=node('div',null,'product-gallery-stage'),main=node('img');main.alt='Main product image';main.hidden=true;
        const empty=node('button',null,'product-gallery-empty');empty.type='button';empty.innerHTML=svg('ImagePlus')+'<span>Add Product Image</span>';empty.onclick=()=>fileInput.click();
        const previewStatus=node('p',null,'small text-muted');previewStatus.setAttribute('role','status');
        const caption=node('button',null,'product-gallery-caption');caption.type='button';caption.hidden=true;caption.onclick=()=>{const row=displayRows()[selected]||displayRows()[0];if(row)view(row,caption);};
        const thumbnails=node('div',null,'product-gallery-thumbnails'),add=node('button','+ Add image','product-gallery-add');add.type='button';add.onclick=()=>fileInput.click();
        const status=node('p',null,'small text-muted mt-2');status.setAttribute('role','status');
        const association=node('select',null,'form-select mt-2');association.setAttribute('aria-label','Variant for selected image');association.hidden=true;
        const replacement=node('input');replacement.type='file';replacement.accept='image/jpeg,image/png,image/webp';replacement.hidden=true;replacement.setAttribute('aria-label','Replacement product image');
        stage.append(main,empty,previewStatus,caption,association);host.append(stage,thumbnails,status,replacement);fileInput.after(host);fileInput.multiple=true;fileInput.hidden=true;
        const label=document.querySelector('label[for="'+fileInput.id+'"]');if(label)label.hidden=true;const help=document.getElementById(fileInput.getAttribute('aria-describedby'));if(help)help.hidden=true;
        let rows=[],selected=0,variantNames=[],replaceId=null,version=0,processing=0,queue=Promise.resolve(),tooltips=[],viewer=null;
        const safeUrl=value=>{if(!value?.trim())return '';try{const url=new URL(value,location.href);return ['http:','https:'].includes(url.protocol)&&!url.username&&!url.password?url.href:'';}catch{return '';}};
        const displayRows=()=>rows.length?rows:(safeUrl(urlInput.value)?[{id:'remote-url',url:safeUrl(urlInput.value),fileName:'Image URL',remote:true}]:[]);
        const release=row=>{if(row.file)URL.revokeObjectURL(row.url);};
        const disposeTooltips=()=>{tooltips.forEach(t=>t.dispose());tooltips=[];};
        function action(icon,label,row,handler){const button=node('button',null,'btn btn-sm product-image-action');button.type='button';button.innerHTML=svg(icon);button.setAttribute('aria-label',label);button.dataset.imageAction=icon;button.disabled=processing>0;button.setAttribute('title',label);if(window.bootstrap?.Tooltip)tooltips.push(new bootstrap.Tooltip(button,{title:label,trigger:'hover focus',placement:'top',container:document.body,animation:false}));button.onclick=()=>{tooltips.forEach(t=>t.hide());handler(row,button);};return button;}
        function view(row,opener){
            const dialog=node('dialog',null,'product-image-viewer'),header=node('header'),heading=node('h2','Product Image','h5'),close=node('button','\u00d7','btn btn-sm');dialog.dataset.sfDialog='workflow';close.type='button';close.setAttribute('aria-label','Close image preview');close.onclick=()=>dialog.close();header.append(heading,close);
            const image=node('img');image.alt=row.fileName||'Product image';const message=node('p','Loading image...','small text-muted');message.setAttribute('role','status');image.onload=()=>message.hidden=true;image.onerror=()=>{image.hidden=true;message.hidden=false;message.textContent='The image could not be loaded. Check its address or replace it.';};image.src=row.url;dialog.append(header,image,message);dialog.setAttribute('aria-label','Product image preview');document.body.append(dialog);viewer=dialog;
            dialog.addEventListener('click',event=>{const r=dialog.getBoundingClientRect();if(event.target===dialog&&(event.clientX<r.left||event.clientX>r.right||event.clientY<r.top||event.clientY>r.bottom))dialog.close();});dialog.addEventListener('close',()=>{dialog.remove();if(viewer===dialog)viewer=null;if(opener.isConnected)opener.focus();});dialog.showModal();close.focus();
        }
        function remove(row){if(row.remote){urlInput.value='';urlInput.dispatchEvent(new Event('input',{bubbles:true}));}else{const index=rows.findIndex(value=>value.id===row.id);if(index<0)return;release(rows[index]);rows.splice(index,1);selected=Math.max(0,Math.min(selected,rows.length-1));}render();}
        association.onchange=()=>{if(rows[selected])rows[selected].variantName=association.value||null;};
        function render(){
            disposeTooltips();thumbnails.replaceChildren();const images=displayRows();selected=Math.max(0,Math.min(selected,images.length-1));const row=images[selected];
            association.replaceChildren(new Option('All variants',''),...variantNames.map(name=>new Option(name,name)));association.value=row?.variantName||'';association.hidden=!row||row.remote||!variantNames.length;
            empty.hidden=!!row;empty.disabled=processing>0;main.hidden=!row;caption.hidden=!row;caption.disabled=processing>0;previewStatus.hidden=!row;
            if(row){previewStatus.textContent='Loading image...';main.onload=()=>{main.hidden=false;previewStatus.hidden=true;};main.onerror=()=>{main.hidden=true;previewStatus.hidden=false;previewStatus.textContent='The image could not be loaded. Check its address or replace it.';};main.src=row.url;caption.textContent=row.fileName||'Product image';}else{main.onload=main.onerror=null;main.removeAttribute('src');previewStatus.textContent='';}
            images.forEach((row,index)=>{const tile=node('div',null,'product-gallery-tile');tile.dataset.imageKey=row.id;const preview=node('button',null,'product-gallery-thumbnail');preview.type='button';preview.setAttribute('aria-label','Preview image '+(index+1));preview.setAttribute('aria-pressed',String(index===selected));const image=node('img');image.src=row.url;image.alt=row.fileName||'Product image';const error=node('span','Image could not load','small text-muted');error.hidden=true;image.onerror=()=>{image.hidden=true;error.hidden=false;};preview.append(image,error);preview.onclick=()=>{selected=index;render();};
                const toolbar=node('div',null,'product-image-toolbar');toolbar.setAttribute('role','group');toolbar.setAttribute('aria-label','Actions for image '+(index+1));toolbar.append(action('Eye','View Image',row,view),action('RefreshCw','Replace Image',row,item=>{replaceId=item.id;replacement.value='';replacement.click();}),action('Trash2','Remove Image',row,remove));
                const primary=node('button',index===0?'Main image':'Set as main','product-gallery-main');primary.type='button';primary.disabled=index===0||processing>0;primary.onclick=()=>{rows.splice(index,1);rows.unshift(row);selected=0;render();};tile.append(preview,toolbar,primary);thumbnails.append(tile);
            });add.disabled=images.length>=5||processing>0;add.hidden=!images.length;thumbnails.append(add);status.textContent=processing?'Processing images...':images.length+' / 5 images \u00b7 JPG, PNG or WebP \u00b7 Up to 3 MB each';host.setAttribute('aria-busy',String(processing>0));host.dispatchEvent(new CustomEvent('product-images-changed',{bubbles:true}));
        }
        async function prepare(file){
            if(file.size>3000000)throw new Error('Image must be 3 MB or smaller.');
            if(file.size<1||!['image/jpeg','image/png','image/webp'].includes(file.type))throw new Error('Choose a JPG, PNG or WebP image.');
            const bytes=new Uint8Array(await file.slice(0,12).arrayBuffer()),png=bytes[0]===137&&bytes[1]===80&&bytes[2]===78&&bytes[3]===71,jpeg=bytes[0]===255&&bytes[1]===216&&bytes[2]===255,webp=String.fromCharCode(...bytes.slice(0,4))==='RIFF'&&String.fromCharCode(...bytes.slice(8,12))==='WEBP';
            if(!(file.type==='image/png'&&png||file.type==='image/jpeg'&&jpeg||file.type==='image/webp'&&webp))throw new Error('The file contents do not match its image type.');
            const url=URL.createObjectURL(file);try{const image=new Image();image.src=url;await image.decode();}catch{URL.revokeObjectURL(url);throw new Error('The image could not be processed. Choose another image.');}
            return {id:crypto.randomUUID(),file,fileName:file.name,contentType:file.type,url};
        }
        function process(files,targetId){
            if(!files.length)return;const startVersion=version;processing++;render();queue=queue.then(async()=>{let pending=[];try{const unique=targetId?files.slice(0,1):files.filter((file,index,all)=>!rows.some(row=>row.file&&row.file.name===file.name&&row.file.size===file.size&&row.file.lastModified===file.lastModified)&&all.findIndex(other=>other.name===file.name&&other.size===file.size&&other.lastModified===file.lastModified)===index);
                if(!targetId&&rows.length+unique.length>5)throw new Error('Upload at most five images. Remove an image before adding another.');
                for(const file of unique)pending.push(await prepare(file));if(startVersion!==version){pending.forEach(release);return;}
                if(targetId&&pending[0]){if(targetId==='remote-url'){urlInput.value='';rows.push(pending[0]);selected=rows.length-1;}else{const index=rows.findIndex(row=>row.id===targetId);if(index<0){pending.forEach(release);return;}const original=rows[index];pending[0].id=original.id;pending[0].variantName=original.variantName;rows[index]=pending[0];release(original);}}
                else rows.push(...pending);pending=[];render();
            }catch(error){pending.forEach(release);if(startVersion===version){render();status.textContent=error.message;}}finally{processing--;host.setAttribute('aria-busy',String(processing>0));host.querySelectorAll('.product-image-action').forEach(button=>button.disabled=processing>0);empty.disabled=processing>0;caption.disabled=processing>0;host.querySelectorAll('.product-gallery-main').forEach((button,index)=>button.disabled=index===0||processing>0);add.disabled=displayRows().length>=5||processing>0;if(startVersion===version&&status.textContent==='Processing images...')status.textContent=displayRows().length+' / 5 images \u00b7 JPG, PNG or WebP \u00b7 Up to 3 MB each';}});
        }
        fileInput.addEventListener('change',()=>{const files=[...fileInput.files];fileInput.value='';process(files);});replacement.addEventListener('change',()=>{const files=[...replacement.files],target=replaceId;replacement.value='';replaceId=null;process(files,target);});
        urlInput.addEventListener('input',()=>{if(!rows.length){render();if(urlInput.value.trim()&&!safeUrl(urlInput.value))status.textContent='Enter a valid HTTP or HTTPS image URL.';}});
        function reset(){version++;viewer?.close();rows.forEach(release);rows=[];selected=0;replaceId=null;render();}
        fileInput.form.addEventListener('reset',()=>{reset();setTimeout(()=>{if(!rows.length)render();},0);});fileInput.closest('dialog')?.addEventListener('close',reset);render();
        return {signature(){return JSON.stringify(rows.map(row=>({imageId:row.imageId||null,isLegacy:row.isLegacy===true,fileName:row.fileName||'',variantName:row.variantName||null,local:row.file?row.id:null})));},renameVariant(previous,next){for(const row of rows)if(row.variantName===previous)row.variantName=next;render();},setVariantOptions(names){variantNames=[...new Set(names)];render();},setSaved(url,name,images=[]){reset();if(images.length)rows=images.map(image=>({...image,id:crypto.randomUUID(),url:safeUrl(image.url)}));else if(name&&url)rows=[{id:crypto.randomUUID(),isLegacy:true,fileName:name,url:safeUrl(url)}];selected=0;render();},async read(){await queue;return Promise.all(rows.map(async row=>{if(!row.file)return {imageId:row.imageId||null,isLegacy:row.isLegacy===true,variantName:row.variantName||null};if(!row.dataPromise)row.dataPromise=new Promise((resolve,reject)=>{const reader=new FileReader();reader.onload=()=>resolve(String(reader.result).split(',')[1]);reader.onerror=()=>{row.dataPromise=null;reject(new Error('Unable to read '+row.fileName));};reader.readAsDataURL(row.file);});return {fileName:row.fileName,contentType:row.contentType,data:await row.dataPromise,variantName:row.variantName||null};}));}};
    }
};
// Preview local uploads and saved images inside their existing form.
window.ProductImagePreview = {
    bind(fileInput, urlInput, settings = {}) {
        if(document.body.dataset.productImageGallery==='true')return ProductImageGallery.bind(fileInput,urlInput);
        const container = document.createElement('div');
        container.className = 'mt-2';
        container.hidden = true;
        const link = document.createElement('a');
        link.className = 'd-block text-break';
        link.setAttribute('aria-expanded', 'false');
        const image = document.createElement('img');
        image.alt = 'Product image preview';
        image.className = 'mt-2 rounded';
        image.style.cssText = 'max-width:100%;width:auto;max-height:220px;object-fit:contain';
        image.hidden = true;
        const status = document.createElement('small');
        status.className = 'text-muted d-block';
        status.setAttribute('role', 'status');
        const remove=document.createElement('button');remove.type='button';remove.className='btn btn-outline-danger btn-sm mt-2';remove.textContent='Remove image';
        container.append(link, image, remove, status);
        fileInput.parentElement.append(container);
        let objectUrl = null, savedUrl = '', savedName = '', originalUrl = '';
        let removed=false;
        remove.onclick=()=>{
            removed=true;fileInput.value='';urlInput.value='';savedUrl=savedName=originalUrl='';
            update();fileInput.dispatchEvent(new Event('change',{bubbles:true}));urlInput.dispatchEvent(new Event('input',{bubbles:true}));fileInput.focus();
        };
        function safeUrl(value) {
            if (!value) return '';
            try {
                const url = new URL(value, location.href);
                return ['http:', 'https:'].includes(url.protocol) ? url.href : '';
            } catch { return ''; }
        }
        function release() {
            if (objectUrl) URL.revokeObjectURL(objectUrl);
            objectUrl = null;
        }
        function show() {
            image.src = link.href;
            image.hidden = false;
            link.setAttribute('aria-expanded', 'true');
            status.textContent = '';
        }
        function update(display = false) {
            release();
            image.removeAttribute('src');
            image.hidden = true;
            link.setAttribute('aria-expanded', 'false');
            status.textContent = '';
            const file = fileInput.files[0];
            let source = '', name = '';
            if (file) {
                if (file.size > 3000000 || !['image/jpeg', 'image/png', 'image/webp'].includes(file.type)) {
                    container.hidden = false;
                    link.hidden = true;
                    status.textContent = 'Choose a JPG, PNG or WebP image up to 3 MB.';
                    return;
                }
                source = objectUrl = URL.createObjectURL(file);
                name = file.name;
            } else {
                source = savedName && urlInput.value === originalUrl ? safeUrl(savedUrl) : safeUrl(urlInput.value.trim()) || safeUrl(savedUrl);
                name = source === safeUrl(savedUrl) && savedName ? savedName : source;
            }
            container.hidden = !source;
            link.hidden = !source;
            if (!source) { link.removeAttribute('href'); return; }
            link.href = source;
            link.textContent = name;
            if (display) show();
        }
        link.addEventListener('click', event => { event.preventDefault(); show(); });
        image.addEventListener('error', () => {
            if (!image.hasAttribute('src')) return;
            image.hidden = true;
            status.textContent = 'The image could not be loaded. Check its address or choose another file.';
        });
        fileInput.addEventListener('change', () => {if(fileInput.files.length)removed=false;update(settings.autoPreview === true);});
        urlInput.addEventListener('input', () => update(settings.autoPreview === true));
        fileInput.form.addEventListener('reset', () => {
            savedUrl = savedName = originalUrl = '';
            removed=false;
            release();
            container.hidden = true;
            image.hidden = true;
            image.removeAttribute('src');
        });
        fileInput.closest('dialog')?.addEventListener('close', () => { release(); image.removeAttribute('src'); });
        return { signature(){return JSON.stringify({removed,url:urlInput.value,files:Array.from(fileInput.files).map(f=>[f.name,f.size,f.lastModified])});},removed(){return removed;},setSaved(url, name = '') { removed=false;savedUrl = url || ''; savedName = name || ''; originalUrl = urlInput.value; update(true); } };
    }
};
