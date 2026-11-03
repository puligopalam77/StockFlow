window.BrandCategories = (() => {
    const bindings = new WeakMap();
    function bind(category, brand, brands) {
        if (!brand) {
            return;
        }
        const previous = bindings.get(brand);
        if (previous) {
            previous();
        }
        if(brand.id==='quick-brand'&&document.body.dataset.catalogRegistrationEnhancements==='true'){
            const active=brands.filter(item=>item.isActive!==false),selected=brand.value,shown=active.slice(0,50),current=active.find(item=>String(item.brandId)===selected);if(current&&!shown.includes(current))shown.push(current);
            brand.replaceChildren(new Option('No Brand / Unbranded',''),...shown.map(item=>new Option(item.brandName,item.brandId)));brand.value=current?selected:'';
            brand.dispatchEvent(new CustomEvent('brand-options-updated',{detail:active}));return;
        }
        if(brand.id!=='quick-brand'&&window.ProductRegistrationForm?.brandPicker){
            document.getElementById(brand.id+'-search')?.remove();
            document.getElementById(brand.id+'-all-brands')?.closest('label')?.remove();
            document.getElementById(brand.id+'-categories-help')?.remove();
            const selected=brand.value,active=brands.filter(item=>item.isActive!==false||String(item.brandId)===selected),shown=active.slice(0,50),current=active.find(item=>String(item.brandId)===selected);
            if(current&&!shown.includes(current))shown.push(current);
            brand.replaceChildren(new Option('No Brand / Unbranded',''),...shown.map(item=>new Option(item.brandName,item.brandId)));brand.value=current?selected:'';
            ProductRegistrationForm.brandPicker(brand,active);return;
        }
        let help = document.getElementById(brand.id + '-categories-help');
        if (!help) {
            help = document.createElement('small');
            help.id = brand.id + '-categories-help';
            help.className = 'text-muted d-block mt-1';
            brand.after(help);
            brand.setAttribute(
                'aria-describedby',
                [brand.getAttribute('aria-describedby'), help.id].filter(Boolean).join(' ')
            );
        }
        const enhanced=document.body.dataset.catalogRegistrationEnhancements==='true';
        const integrated=!!window.ProductRegistrationForm?.brandPicker;
        let all= document.getElementById(brand.id+'-all-brands');
        if(enhanced&&!all){
            const label=document.createElement('label'); label.className='small d-block mt-2';
            all=document.createElement('input');all.type='checkbox';all.id=brand.id+'-all-brands';
            label.append(all,document.createTextNode(' Show all brands'));help.before(label);
        }
        if(all){
            const selected=brands.find(value=>String(value.brandId)===brand.value);
            all.checked=all.checked||!!selected&&(selected.categoryIds||[]).length>0&&!(selected.applicableCategoryIds||selected.categoryIds||[]).includes(Number(category.value));
            all.onchange=update;
        }
        let search=document.getElementById(brand.id+'-search');if(integrated){search?.remove();search=null;help.hidden=true;}else{if(!search){search=document.createElement('input');search.id=brand.id+'-search';search.type='search';search.className='form-control mb-2';search.placeholder='Search brands';search.setAttribute('aria-label','Search brands');brand.before(search);}search.oninput=update;}
        function update() {
            const selected = brand.value;
            const id = Number(category.value);
            const available = brands.filter((value) => {
                return value.isActive !== false && (((enhanced?value.applicableCategoryIds:value.categoryIds) || value.categoryIds || []).includes(id) || enhanced && (!(value.categoryIds || []).length || all?.checked));
            });
            const term=search?.value.trim().toLowerCase()||'';const shown=available.filter(b=>b.brandName.toLowerCase().includes(term)).slice(0,50);const current=available.find(b=>String(b.brandId)===selected);if(current&&!shown.includes(current))shown.push(current);
            brand.replaceChildren(
                new Option(integrated?'No Brand / Unbranded':'None', ''),
                ...shown.map((value) => {
                    return new Option(value.brandName, value.brandId);
                })
            );
            brand.value = available.some((value) => {
                return String(value.brandId) === selected;
            })
                ? selected
                : '';
            if(integrated){
                const allLabel=all?.closest('label'),wasOpen=brand.closest('.product-category-select')?.querySelector('.product-category-select-panel')?.hidden===false;
                ProductRegistrationForm.brandPicker(brand,available);
                const host=brand.closest('.product-category-select');
                if(allLabel)host.querySelector('.product-category-select-panel').append(allLabel);
                if(wasOpen)host.querySelector('.product-category-select-toggle').click();
            }
            if(enhanced){help.textContent=available.length?'':'No brands for this category. Try Show all brands.';return;} else help.textContent = !id
                ? 'Choose a product group to see its brands.'
                : !available.length
                  ? 'No brand is set for this product group. Choose the group in Brands, or leave Brand as None.'
                  : 'Only brands saved for this product group are shown.';
        }
        category.addEventListener('change', update);
        bindings.set(brand, () => {
            return category.removeEventListener('change', update);
        });
        update();
    }
    return {
        bind
    };
})();
