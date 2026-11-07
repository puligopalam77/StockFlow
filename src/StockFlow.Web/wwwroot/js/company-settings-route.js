// URL fragments never reach Razor. Resolve old bookmarks before displaying a view.
(() => {
 const url=new URL(location.href);
 if(url.pathname.toLowerCase()!=='/settings/company'||url.searchParams.has('section'))return;
 const section={'#company-business-information':'business','#company-billing-settings':'billing','#billing-policy-settings':'billing'}[url.hash];
 if(!section)return;
 url.searchParams.set('section',section);url.hash='';
 document.documentElement.style.visibility='hidden';location.replace(url.pathname+url.search);
})();
