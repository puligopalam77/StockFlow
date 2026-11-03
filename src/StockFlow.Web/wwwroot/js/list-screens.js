(() => {
    const main = document.querySelector('.sf-list-screen');
    if (!main) return;
    // Keep each page's own heading and buttons together, inside its existing workspace.
    // Move the breadcrumb instead of the heading so page event delegation stays intact.
    const content = main.querySelector('.configuration-content') || main;
    const title = [...content.querySelectorAll('h1, .configuration-heading h2')]
        .find(element => !element.closest('dialog, [role="dialog"], #editor'));
    if (!title) return;
    let heading = title.closest('.sf-page-heading, .configuration-heading, .admin-heading, .reports-header');
    if (!heading) {
        heading = document.createElement('header');
        title.before(heading);
        const description = title.nextElementSibling;
        heading.append(title);
        if (description?.matches('p') && !description.id && !description.matches('[role]')) heading.append(description);
    }
    heading.classList.add('sf-list-caption');
    const breadcrumb = main.querySelector('.sf-breadcrumb');
    if (breadcrumb) heading.after(breadcrumb);
    // The summary dashboard sizes its toolbar to fit its charts within the viewport.
    if (content.querySelector('#shop-summary')) return;
    for (const filter of content.querySelectorAll('.sf-filter-bar, .admin-filters, form[id$="-search"], form[id$="-filter"], #global-search, #comparison-form, #lookup-form, #warranty-search, #master-list .row')) {
        if (!filter.closest('dialog, [role="dialog"], #editor')) filter.classList.add('sf-list-filter');
    }
    for (const search of content.querySelectorAll('#master-search, #variant-search')) search.closest('.row')?.classList.add('sf-list-filter');
})();
