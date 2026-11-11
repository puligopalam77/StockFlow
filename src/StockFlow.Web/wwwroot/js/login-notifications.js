// Presentation only: login keeps its normal antiforgery-protected form POST.
(() => {
    const form=document.querySelector('.sf-login-form');
    if(!form||form.dataset.notificationsReady)return;
    form.dataset.notificationsReady='true';
    const notify=(type,message,key,duration=5500)=>StockFlowNotify.show(type,message,{key,duration});
    const url=new URL(location.href),reason=url.searchParams.get('reason');
    const expired=reason==='idle'||reason==='absolute',ended=reason==='ended';
    const passwordChanged=url.searchParams.get('passwordChanged')==='True';
    // Consume only notification metadata; preserve ReturnUrl and all other query
    // parameters. Refresh/back navigation must not announce the same expiry again.
    if(expired||ended||passwordChanged){
        if(expired||ended)url.searchParams.delete('reason');
        if(passwordChanged)url.searchParams.delete('passwordChanged');
        history.replaceState(history.state,'',url.pathname+url.search+url.hash);
    }
    if(expired)notify('info','Your session has expired. Please sign in again.','login-session');
    else if(ended)notify('info','Your session has ended. Please sign in again.','login-session');
    if(passwordChanged)notify('success','Password updated. Sign in with your new password.','login-password');
    const errors=[...document.getElementById('login-feedback').content.querySelectorAll('li')]
        .map(item=>item.textContent.trim()).filter(Boolean);
    if(errors.length)notify('error',[...new Set(errors)].join('\n'),'login-error',0);
    const offline=()=>notify('error','Unable to connect. Check your internet connection and try again.','login-network',0);
    window.addEventListener('offline',offline);
    window.addEventListener('online',()=>StockFlowNotify.dismiss('login-network'));
    form.addEventListener('submit',event=>{
        if(!navigator.onLine){event.preventDefault();offline();}
    });
})();
