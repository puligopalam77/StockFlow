(() => {
 'use strict';
 const datedCards = [...document.querySelectorAll('[data-report-url]')].filter(card => ['/Sales', '/Reports/Activity', '/Reports/ProductSales'].includes(card.dataset.reportUrl));
 const from = document.getElementById('report-from'), to = document.getElementById('report-to');
 const invalid = () => !from.value || !to.value || from.value > to.value || !from.validity.valid || !to.validity.valid;
 StockFlowReportDates.bind('report', () => {
  document.querySelectorAll('[data-custom-date]').forEach(label => { label.hidden = document.getElementById('report-period').value !== 'custom'; });
  document.getElementById('reports-date-error').hidden = !invalid();
  datedCards.forEach(card => {
   const url = new URL(card.dataset.reportUrl, location.origin);
   if (!invalid()) { url.searchParams.set('from', from.value); url.searchParams.set('to', to.value); }
   card.href = url.pathname + url.search;
  });
 });
 datedCards.forEach(card => card.addEventListener('click', event => {
  if (invalid()) { event.preventDefault(); document.getElementById('reports-date-error').hidden = false; from.focus(); }
 }));
})();
