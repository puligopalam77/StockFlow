exports.fillAddress=async(locator,text)=>{
 if(!await locator.evaluate(e=>!!e.sfAddressCombined)){await locator.fill(text);return text;}
 const id=await locator.evaluate(e=>e.sfAddressCombined.container.id),details=locator.page().locator('#'+id);
 if(await details.evaluate(e=>e.tagName==='DETAILS'&&!e.open))await details.locator(':scope > summary').click();
 const replace=details.getByRole('button',{name:'Enter Address Details',exact:true});if(await replace.isVisible())await replace.click();
 await details.locator('input[id$="-line1"]').fill(text);return locator.inputValue();
};
exports.expectFixedCountry=async(source,expected='India')=>{
 const assert=require('node:assert/strict');
 const state=await source.evaluate(n=>{const display=n.sfCountryField?.display||n,s=getComputedStyle(display);return {value:display.value,readonly:display.readOnly,tab:display.tabIndex,bg:s.backgroundColor,color:s.color,arrow:s.backgroundImage,pointer:s.pointerEvents,sourceValue:n.value};});
 assert.equal(state.value,expected);assert(state.readonly);assert.equal(state.tab,-1);assert.equal(state.bg,'rgb(243, 244, 246)');assert.equal(state.color,'rgb(71, 85, 105)');assert.equal(state.arrow,'none');assert.equal(state.pointer,'none');
 return state;
};
exports.fillLocation=async(locator,value)=>{
 const id=await locator.evaluate(e=>e.sfAddressPicker?.input.id);if(!id){if(await locator.evaluate(e=>e.tagName==='SELECT'))await locator.selectOption(value);else await locator.fill(value);return;}
 const rawLabel=await locator.evaluate((e,value)=>e.tagName==='SELECT'?[...e.options].find(o=>o.value===value)?.textContent||value:value,value);
 const label=rawLabel.replace(/^\s*\d{2}\s*[-–—:.)·]*\s*/,'').replace(/\s*\(?\d{2}\)?\s*$/,'').trim();
 const search=locator.page().locator('#'+id);await search.click();await search.fill(label);
 await locator.page().locator('#'+id+'-options').getByRole('option',{name:label,exact:true}).click();
};
// Exercise the visible shared control instead of forcing a hidden native source.
exports.selectValue=async(locator,value)=>{
 const picker=await locator.evaluate(e=>e.sfSelect?{trigger:e.sfSelect.input.id,query:e.sfSelect.search.id,list:e.sfSelect.list.id}:null);
 if(!picker)return locator.selectOption(value);
 const option=await locator.evaluate((e,v)=>[...e.options].find(o=>o.value===String(v))?.textContent||String(v),value);
 await locator.page().locator('#'+picker.trigger).click();
 await locator.page().locator('#'+picker.query).fill(option);
 const options=locator.page().locator('#'+picker.list+' [role=option]');
 for(let index=0;index<await options.count();index++)if(await options.nth(index).getAttribute('data-value')===String(value)){await options.nth(index).click();return;}
 throw Error('Requested option is unavailable: '+value);
};
