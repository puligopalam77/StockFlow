// Translate interface captions only. Product names, user entries and saved codes stay as entered.
window.StockFlowLanguage = (() => {
    const captions = {
        'Language settings': ['భాష సెట్టింగులు', 'भाषा सेटिंग्स'],
        Configuration: ['సెట్టింగులు', 'सेटिंग्स'],
        Language: ['భాష', 'भाषा'],
        'Save language': ['భాషను సేవ్ చేయండి', 'भाषा सहेजें'],
        Dashboard: ['ముఖ్య సమాచారం', 'मुख्य जानकारी'],
        'Receive Stock': ['స్టాక్ స్వీకరించండి', 'स्टॉक प्राप्त करें'],
        'Sell / Billing': ['అమ్మకం / బిల్లింగ్', 'बिक्री / बिलिंग'],
        'Stock on hand': ['అందుబాటులో ఉన్న స్టాక్', 'उपलब्ध स्टॉक'],
        Reports: ['నివేదికలు', 'रिपोर्ट'],
        'Repairs & warranty': ['మరమ్మతులు మరియు వారంటీ', 'मरम्मत और वारंटी'],
        Setup: ['సెట్టింగులు', 'सेटिंग्स'],
        'Products & contacts': ['ఉత్పత్తులు మరియు పరిచయాలు', 'उत्पाद और संपर्क'],
        'Receive new stock': ['కొత్త స్టాక్ స్వీకరణ', 'नई स्टॉक प्राप्ति'],
        'Counter billing': ['కౌంటర్ బిల్లింగ్', 'काउंटर बिलिंग'],
        'Add new product': ['కొత్త వస్తువు చేర్చండి', 'नया सामान जोड़ें'],
        'Add another item': ['మరొక వస్తువు చేర్చండి', 'एक और सामान जोड़ें'],
        'Remove item': ['వస్తువును తీసివేయండి', 'सामान हटाएँ'],
        'Product / variant': ['వస్తువు / రకం', 'सामान / प्रकार'],
        'Choose supplier': ['సరఫరాదారును ఎంచుకోండి', 'आपूर्तिकर्ता चुनें'],
        'Choose product / size / model': [
            'వస్తువు / సైజు / మోడల్ ఎంచుకోండి',
            'सामान / आकार / मॉडल चुनें'
        ],
        'Product name, model, item code or barcode': [
            'వస్తువు పేరు, మోడల్, వస్తువు కోడ్ లేదా బార్‌కోడ్',
            'सामान का नाम, मॉडल, सामान कोड या बारकोड'
        ],
        'Tax & line discount': ['పన్ను మరియు వస్తువుపై తగ్గింపు', 'कर और सामान पर छूट'],
        'More options: advance order, discounts and notes': [
            'మరిన్ని వివరాలు: ముందస్తు ఆర్డర్, తగ్గింపులు మరియు గమనికలు',
            'अन्य विकल्प: अग्रिम ऑर्डर, छूट और टिप्पणियाँ'
        ],
        'One PDF, JPG or PNG up to 5 MB.': [
            '5 MB వరకు ఒక PDF, JPG లేదా PNG ఫైల్.',
            '5 MB तक एक PDF, JPG या PNG फ़ाइल।'
        ],
        'Scan or choose the goods received. Check the quantities and cost. Saving adds undamaged items to stock for sale.':
            [
                'అందిన సరుకును స్కాన్ చేయండి లేదా ఎంచుకోండి. సంఖ్య మరియు ధరను తనిఖీ చేయండి. సేవ్ చేసినప్పుడు మంచి సరుకు అమ్మకపు స్టాక్‌లో చేరుతుంది.',
                'प्राप्त सामान स्कैन करें या चुनें। मात्रा और लागत जाँचें। सहेजने पर सही सामान बिक्री के स्टॉक में जुड़ता है।'
            ],
        'Hold this bill': ['ఈ బిల్లు తర్వాత పూర్తి చేయండి', 'इस बिल को बाद के लिए रखें'],
        '1. Add items to the bill': ['1. బిల్లుకు వస్తువులు చేర్చండి', '1. बिल में सामान जोड़ें'],
        '2. Customer': ['2. కస్టమర్', '2. ग्राहक'],
        '3. Payment': ['3. చెల్లింపు', '3. भुगतान'],
        'Find by name or mobile': ['పేరు లేదా మొబైల్‌తో వెతకండి', 'नाम या मोबाइल से खोजें'],
        'Name or mobile': ['పేరు లేదా మొబైల్', 'नाम या मोबाइल'],
        'Bill to': ['బిల్లు ఎవరికి', 'बिल किसके नाम पर'],
        'Walk-in customer': ['సాధారణ కస్టమర్', 'बिना नाम वाला ग्राहक'],
        'Add selected item': ['ఎంచుకున్న వస్తువును చేర్చండి', 'चुना हुआ सामान जोड़ें'],
        'Items in this bill': ['ఈ బిల్లులోని వస్తువులు', 'इस बिल का सामान'],
        'Product / size / model': ['వస్తువు / సైజు / మోడల్', 'सामान / आकार / मॉडल'],
        'Choose a product': ['వస్తువును ఎంచుకోండి', 'सामान चुनें'],
        'Find an item without scanning': [
            'స్కాన్ చేయకుండా వస్తువును వెతకండి',
            'स्कैन किए बिना सामान खोजें'
        ],
        'Product name, size, color or item code': [
            'వస్తువు పేరు, సైజు, రంగు లేదా వస్తువు కోడ్',
            'सामान का नाम, आकार, रंग या सामान कोड'
        ],
        'Scan items, choose a customer or walk-in, then take payment and open the invoice.': [
            'వస్తువులను స్కాన్ చేసి కస్టమర్‌ను ఎంచుకోండి. తర్వాత చెల్లింపు తీసుకుని బిల్లు తెరవండి.',
            'सामान स्कैन करें और ग्राहक चुनें। फिर भुगतान लें और बिल खोलें।'
        ],
        'Items you add appear here. Check the product, quantity and price before taking payment.': [
            'చేర్చిన వస్తువులు ఇక్కడ కనిపిస్తాయి. చెల్లింపు తీసుకునే ముందు వస్తువు, సంఖ్య మరియు ధరను తనిఖీ చేయండి.',
            'जोड़ा गया सामान यहाँ दिखेगा। भुगतान लेने से पहले सामान, मात्रा और कीमत जाँचें।'
        ],
        Supplier: ['సరఫరాదారు', 'आपूर्तिकर्ता'],
        'Supplier invoice number': ['సరఫరాదారు బిల్లు నంబర్', 'आपूर्तिकर्ता का बिल नंबर'],
        'Invoice date': ['బిల్లు తేదీ', 'बिल की तारीख'],
        'Order / purchase date': ['ఆర్డర్ / కొనుగోలు తేదీ', 'ऑर्डर / खरीद की तारीख'],
        'Goods received date': ['సరుకు అందిన తేదీ', 'माल मिलने की तारीख'],
        'Supplier invoice attachment': ['సరఫరాదారు బిల్లు ఫైల్', 'आपूर्तिकर्ता की बिल फ़ाइल'],
        'Items received': ['అందిన వస్తువులు', 'प्राप्त सामान'],
        'Scan product to receive stock': [
            'స్టాక్ స్వీకరించడానికి వస్తువును స్కాన్ చేయండి',
            'स्टॉक प्राप्त करने के लिए सामान स्कैन करें'
        ],
        'Scan product to sell': [
            'అమ్మడానికి వస్తువును స్కాన్ చేయండి',
            'बेचने के लिए सामान स्कैन करें'
        ],
        'Find an existing item': ['ఉన్న వస్తువును వెతకండి', 'मौजूदा सामान खोजें'],
        'Expected quantity': ['రావాల్సిన సంఖ్య', 'अपेक्षित मात्रा'],
        'Received (good)': ['అందిన మంచి వస్తువులు', 'प्राप्त सही सामान'],
        Damaged: ['దెబ్బతిన్నవి', 'खराब सामान'],
        'Click here, then scan': [
            'ఇక్కడ క్లిక్ చేసి స్కాన్ చేయండి',
            'यहाँ क्लिक करें, फिर स्कैन करें'
        ],
        'Click the box, then scan the product barcode. You can also type the code and press Enter. Scan the same product again to add one more. If it is new, enter its details first.':
            [
                'పెట్టెలో క్లిక్ చేసి వస్తువు బార్‌కోడ్ స్కాన్ చేయండి. కోడ్ టైప్ చేసి Enter నొక్కవచ్చు. మరొకటి చేర్చడానికి మళ్లీ స్కాన్ చేయండి. కొత్త వస్తువు అయితే మొదట వివరాలు నమోదు చేయండి.',
                'बॉक्स में क्लिक करके सामान का बारकोड स्कैन करें। कोड लिखकर Enter भी दबा सकते हैं। एक और सामान जोड़ने के लिए फिर स्कैन करें। नया सामान हो तो पहले उसका विवरण दर्ज करें।'
            ],
        'Click the box, then scan the barcode. You can also type the code and press Enter. Scan the same product again to add one more. For items with a serial number, scan the serial label on the item being sold.':
            [
                'పెట్టెలో క్లిక్ చేసి బార్‌కోడ్ స్కాన్ చేయండి. కోడ్ టైప్ చేసి Enter నొక్కవచ్చు. మరొకటి చేర్చడానికి మళ్లీ స్కాన్ చేయండి. సీరియల్ నంబర్ ఉన్న వస్తువు అయితే అమ్మే వస్తువు సీరియల్ లేబుల్ స్కాన్ చేయండి.',
                'बॉक्स में क्लिक करके बारकोड स्कैन करें। कोड लिखकर Enter भी दबा सकते हैं। एक और सामान जोड़ने के लिए फिर स्कैन करें। सीरियल नंबर वाले सामान के लिए उसी सामान का सीरियल लेबल स्कैन करें।'
            ],
        Dashboard: ['డ్యాష్‌బోర్డ్', 'डैशबोर्ड'],
        'Receive Stock': ['స్టాక్ స్వీకరించండి', 'स्टॉक प्राप्त करें'],
        'Sell / Billing': ['అమ్మకం / బిల్లింగ్', 'बिक्री / बिलिंग'],
        'Stock on hand': ['అందుబాటులో ఉన్న స్టాక్', 'उपलब्ध स्टॉक'],
        Reports: ['నివేదికలు', 'रिपोर्ट'],
        Setup: ['సెట్టింగులు', 'सेटिंग'],
        'Repairs & warranty': ['మరమ్మతులు & వారంటీ', 'मरम्मत और वारंटी'],
        'Products & contacts': ['ఉత్పత్తులు & పరిచయాలు', 'उत्पाद और संपर्क'],
        Products: ['ఉత్పత్తులు', 'उत्पाद'],
        Customers: ['కస్టమర్లు', 'ग्राहक'],
        Suppliers: ['సరఫరాదారులు', 'आपूर्तिकर्ता'],
        Categories: ['వర్గాలు', 'श्रेणियाँ'],
        Brands: ['బ్రాండ్లు', 'ब्रांड'],
        'Customer Name': ['కస్టమర్ పేరు', 'ग्राहक का नाम'],
        'Supplier Name': ['సరఫరాదారు పేరు', 'आपूर्तिकर्ता का नाम'],
        'Category Name': ['వర్గం పేరు', 'श्रेणी का नाम'],
        'Brand Name': ['బ్రాండ్ పేరు', 'ब्रांड का नाम'],
        'Product name': ['ఉత్పత్తి పేరు', 'उत्पाद का नाम'],
        Product: ['ఉత్పత్తి', 'उत्पाद'],
        Category: ['వర్గం', 'श्रेणी'],
        Brand: ['బ్రాండ్', 'ब्रांड'],
        Customer: ['కస్టమర్', 'ग्राहक'],
        Supplier: ['సరఫరాదారు', 'आपूर्तिकर्ता'],
        'Mobile Number': ['మొబైల్ నంబర్', 'मोबाइल नंबर'],
        'Alternate Mobile': ['మరో మొబైల్ నంబర్', 'वैकल्पिक मोबाइल नंबर'],
        Mobile: ['మొబైల్', 'मोबाइल'],
        'Mobile (optional)': ['మొబైల్ (ఐచ్ఛికం)', 'मोबाइल (वैकल्पिक)'],
        Phone: ['ఫోన్ నంబర్', 'फोन नंबर'],
        Email: ['ఇమెయిల్', 'ईमेल'],
        Address: ['చిరునామా', 'पता'],
        'Address (optional)': ['చిరునామా (ఐచ్ఛికం)', 'पता (वैकल्पिक)'],
        'GST Number': ['జీఎస్టీ నంబర్', 'जीएसटी नंबर'],
        'GST number': ['జీఎస్టీ నంబర్', 'जीएसटी नंबर'],
        'GSTIN (India)': ['జీఎస్టీఐఎన్ (భారతదేశం)', 'जीएसटीआईएन (भारत)'],
        'GSTIN (optional)': ['జీఎస్టీఐఎన్ (ఐచ్ఛికం)', 'जीएसटीआईएन (वैकल्पिक)'],
        Name: ['పేరు', 'नाम'],
        Description: ['వివరణ', 'विवरण'],
        Notes: ['గమనికలు', 'टिप्पणी'],
        Country: ['దేశం', 'देश'],
        'State / Province': ['రాష్ట్రం', 'राज्य'],
        City: ['నగరం', 'शहर'],
        State: ['రాష్ట్రం', 'राज्य'],
        'PIN / postal code': ['పిన్ / పోస్టల్ కోడ్', 'पिन / पोस्टल कोड'],
        'Contact Person': ['సంప్రదించాల్సిన వ్యక్తి', 'संपर्क व्यक्ति'],
        'Supplier Type': ['సరఫరాదారు రకం', 'आपूर्तिकर्ता का प्रकार'],
        'Supplier Code': ['సరఫరాదారు కోడ్', 'आपूर्तिकर्ता कोड'],
        'Basic information': ['ప్రాథమిక సమాచారం', 'मूल जानकारी'],
        'Contact information': ['సంప్రదింపు వివరాలు', 'संपर्क जानकारी'],
        'Address & tax': ['చిరునామా & పన్ను', 'पता और कर'],
        'Purchasing details': ['కొనుగోలు వివరాలు', 'खरीद की जानकारी'],
        'When should we pay?': ['చెల్లింపు ఎప్పుడు చేయాలి?', 'भुगतान कब करना है?'],
        'Number of days until payment': ['చెల్లింపుకు మిగిలిన రోజులు', 'भुगतान तक दिनों की संख्या'],
        Active: ['అమలులో ఉంది', 'सक्रिय'],
        Status: ['స్థితి', 'स्थिति'],
        Actions: ['చర్యలు', 'कार्य'],
        Save: ['సేవ్ చేయండి', 'सहेजें'],
        Cancel: ['రద్దు చేయండి', 'रद्द करें'],
        Close: ['మూసివేయండి', 'बंद करें'],
        Edit: ['సవరించండి', 'बदलें'],
        Search: ['వెతకండి', 'खोजें'],
        Find: ['కనుగొనండి', 'खोजें'],
        'Look up': ['వివరాలు చూడండి', 'जानकारी खोजें'],
        'Find product': ['ఉత్పత్తిని వెతకండి', 'उत्पाद खोजें'],
        'Find items': ['వస్తువులను వెతకండి', 'वस्तुएँ खोजें'],
        'Add item': ['వస్తువును జోడించండి', 'वस्तु जोड़ें'],
        'Add items': ['వస్తువులను జోడించండి', 'वस्तुएँ जोड़ें'],
        Remove: ['తొలగించండి', 'हटाएँ'],
        Previous: ['మునుపటి', 'पिछला'],
        Next: ['తరువాత', 'अगला'],
        Back: ['వెనుకకు', 'वापस'],
        Refresh: ['తాజా వివరాలు చూడండి', 'ताज़ा करें'],
        Print: ['ప్రింట్ చేయండి', 'प्रिंट करें'],
        Labels: ['లేబుళ్లు', 'लेबल'],
        Quantity: ['పరిమాణం', 'मात्रा'],
        'Expected quantity': ['ఆశించిన పరిమాణం', 'अपेक्षित मात्रा'],
        'Received (good)': ['స్వీకరించిన మంచి వస్తువులు', 'प्राप्त अच्छी वस्तुएँ'],
        Damaged: ['పాడైన వస్తువులు', 'क्षतिग्रस्त'],
        'Purchase cost': ['కొనుగోలు ధర', 'खरीद मूल्य'],
        'Purchase cost per item': ['ఒక్క వస్తువు కొనుగోలు ధర', 'प्रति वस्तु खरीद मूल्य'],
        'Selling price': ['అమ్మకపు ధర', 'बिक्री मूल्य'],
        'Selling price per item': ['ఒక్క వస్తువు అమ్మకపు ధర', 'प्रति वस्तु बिक्री मूल्य'],
        'Tax %': ['పన్ను %', 'कर %'],
        'Warranty months': ['వారంటీ నెలలు', 'वारंटी के महीने'],
        'Item code / SKU': ['వస్తువు కోడ్ / SKU', 'वस्तु कोड / SKU'],
        'Size / model name': ['సైజు / మోడల్ పేరు', 'आकार / मॉडल का नाम'],
        'Add size / model': ['సైజు / మోడల్ జోడించండి', 'आकार / मॉडल जोड़ें'],
        'Edit size / model': ['సైజు / మోడల్ సవరించండి', 'आकार / मॉडल बदलें'],
        'Save Variant': ['సైజు / మోడల్ సేవ్ చేయండి', 'आकार / मॉडल सहेजें'],
        'New sale': ['కొత్త అమ్మకం', 'नई बिक्री'],
        'Sales & invoices': ['అమ్మకాలు & బిల్లులు', 'बिक्री और बिल'],
        'Receive stock': ['స్టాక్ స్వీకరించండి', 'स्टॉक प्राप्त करें'],
        'Receipt details': ['స్వీకరణ వివరాలు', 'प्राप्ति विवरण'],
        'Review receipt': ['స్వీకరణను తనిఖీ చేయండి', 'प्राप्ति की जाँच करें'],
        'Save stock receipt': ['స్టాక్ స్వీకరణ సేవ్ చేయండి', 'स्टॉक प्राप्ति सहेजें'],
        'Save and select customer': ['సేవ్ చేసి కస్టమర్ ఎంచుకోండి', 'सहेजें और ग्राहक चुनें'],
        'Add a new customer': ['కొత్త కస్టమర్ జోడించండి', 'नया ग्राहक जोड़ें'],
        'Complete sale': ['అమ్మకం పూర్తి చేయండి', 'बिक्री पूरी करें'],
        'Complete bill': ['బిల్లు పూర్తి చేయండి', 'बिल पूरा करें'],
        'Hold bill': ['బిల్లు నిలిపి ఉంచండి', 'बिल रोककर रखें'],
        'Saved bills': ['సేవ్ చేసిన బిల్లులు', 'सहेजे गए बिल'],
        Resume: ['కొనసాగించండి', 'जारी रखें'],
        'Payment method': ['చెల్లింపు విధానం', 'भुगतान का तरीका'],
        Cash: ['నగదు', 'नकद'],
        Card: ['కార్డు', 'कार्ड'],
        'Bank transfer': ['బ్యాంకు బదిలీ', 'बैंक हस्तांतरण'],
        'Pay later (credit)': ['తరువాత చెల్లించండి (బాకీ)', 'बाद में भुगतान (उधार)'],
        'Cash received': ['అందుకున్న నగదు', 'प्राप्त नकद'],
        'Payment reference': ['చెల్లింపు రిఫరెన్స్', 'भुगतान संदर्भ'],
        'Stock transfers': ['స్టాక్ బదిలీలు', 'स्टॉक हस्तांतरण'],
        'Create transfer': ['బదిలీ సృష్టించండి', 'हस्तांतरण बनाएँ'],
        'Send goods': ['వస్తువులు పంపండి', 'वस्तुएँ भेजें'],
        'Receive goods': ['వస్తువులు స్వీకరించండి', 'वस्तुएँ प्राप्त करें'],
        'Save draft': ['డ్రాఫ్ట్ సేవ్ చేయండి', 'मसौदा सहेजें'],
        'Low Stock': ['తక్కువ స్టాక్', 'कम स्टॉक'],
        'Stock Receipt History': ['స్టాక్ స్వీకరణ చరిత్ర', 'स्टॉक प्राप्ति इतिहास'],
        'Staff accounts & access': ['సిబ్బంది ఖాతాలు & అనుమతులు', 'कर्मचारी खाते और अनुमति'],
        'Add staff account': ['సిబ్బంది ఖాతా జోడించండి', 'कर्मचारी खाता जोड़ें'],
        'Full name': ['పూర్తి పేరు', 'पूरा नाम'],
        Username: ['యూజర్ పేరు', 'उपयोगकर्ता नाम'],
        Password: ['పాస్‌వర్డ్', 'पासवर्ड'],
        'Confirm password': ['పాస్‌వర్డ్ నిర్ధారించండి', 'पासवर्ड की पुष्टि करें'],
        'Current password': ['ప్రస్తుత పాస్‌వర్డ్', 'वर्तमान पासवर्ड'],
        'New password': ['కొత్త పాస్‌వర్డ్', 'नया पासवर्ड'],
        'Confirm new password': ['కొత్త పాస్‌వర్డ్ నిర్ధారించండి', 'नए पासवर्ड की पुष्टि करें'],
        'Change password': ['పాస్‌వర్డ్ మార్చండి', 'पासवर्ड बदलें'],
        'Save Password': ['పాస్‌వర్డ్ సేవ్ చేయండి', 'पासवर्ड सहेजें'],
        'Sign in': ['సైన్ ఇన్ చేయండి', 'साइन इन करें'],
        'Sign out': ['సైన్ అవుట్ చేయండి', 'साइन आउट करें'],
        'Shop details & bill settings': [
            'షాప్ వివరాలు & బిల్లు సెట్టింగులు',
            'दुकान की जानकारी और बिल सेटिंग'
        ],
        'Company name': ['షాప్ పేరు', 'दुकान का नाम'],
        'Daily cash closing': ['రోజువారీ నగదు ముగింపు', 'दैनिक नकद मिलान'],
        'Closing date': ['నగదు ముగింపు తేదీ', 'मिलान की तारीख'],
        'Refresh cash totals': ['నగదు మొత్తాలు తాజా చేయండి', 'नकद कुल ताज़ा करें'],
        'Print closing': ['నగదు ముగింపు ప్రింట్ చేయండి', 'नकद मिलान प्रिंट करें'],
        'Recorded cash movements': ['నమోదైన నగదు లావాదేవీలు', 'दर्ज नकद लेनदेन'],
        'Cash received from sales': ['అమ్మకాల ద్వారా వచ్చిన నగదు', 'बिक्री से प्राप्त नकद'],
        'Cash received from repairs': ['మరమ్మతుల ద్వారా వచ్చిన నగదు', 'मरम्मत से प्राप्त नकद'],
        'Cash refunded to customers': [
            'కస్టమర్లకు తిరిగి ఇచ్చిన నగదు',
            'ग्राहकों को लौटाया गया नकद'
        ],
        'Cash paid to suppliers': ['సరఫరాదారులకు ఇచ్చిన నగదు', 'आपूर्तिकर्ताओं को दिया गया नकद'],
        'Count and close': ['లెక్కించి ముగించండి', 'गिनें और मिलान करें'],
        'Opening cash': ['ప్రారంభ నగదు', 'शुरुआती नकद'],
        'Extra cash added': ['అదనంగా జోడించిన నగదు', 'अतिरिक्त जोड़ा गया नकद'],
        'Extra cash removed': ['అదనంగా తీసిన నగదు', 'अतिरिक्त निकाला गया नकद'],
        'Counted cash': ['లెక్కించిన నగదు', 'गिना गया नकद'],
        'Reason / explanation': ['కారణం / వివరణ', 'कारण / विवरण'],
        'Expected cash': ['ఉండాల్సిన నగదు', 'अपेक्षित नकद'],
        Difference: ['తేడా', 'अंतर'],
        'Save cash closing': ['నగదు ముగింపు సేవ్ చేయండి', 'नकद मिलान सहेजें'],
        'Save updated closing': ['సవరించిన ముగింపు సేవ్ చేయండి', 'बदला हुआ मिलान सहेजें'],
        'Retry saved closing': [
            'సేవ్ చేసిన ముగింపు మళ్లీ ప్రయత్నించండి',
            'सहेजा हुआ मिलान फिर भेजें'
        ],
        'Saved closing history': ['నగదు ముగింపు చరిత్ర', 'नकद मिलान का इतिहास'],
        Version: ['సంస్కరణ', 'संस्करण'],
        'Saved at': ['సేవ్ చేసిన సమయం', 'सहेजने का समय'],
        Reason: ['కారణం', 'कारण'],
        'Shop pilot checklist': ['షాప్ పరీక్షల జాబితా', 'दुकान परीक्षण सूची'],
        'Shop / location': ['షాప్ / స్థలం', 'दुकान / स्थान'],
        'Tested by': ['పరీక్షించిన వ్యక్తి', 'परीक्षण करने वाला'],
        'Build / deployment date': ['వెర్షన్ / అమలు తేదీ', 'संस्करण / स्थापना तारीख'],
        'Workstation / browser': ['కంప్యూటర్ / బ్రౌజర్', 'कंप्यूटर / ब्राउज़र'],
        'Scanner model / Enter setting': [
            'స్కానర్ మోడల్ / Enter సెట్టింగ్',
            'स्कैनर मॉडल / Enter सेटिंग'
        ],
        'Printer / label size': ['ప్రింటర్ / లేబుల్ సైజు', 'प्रिंटर / लेबल का आकार'],
        'Backup and restore-test evidence': [
            'బ్యాకప్ మరియు పునరుద్ధరణ పరీక్ష ఆధారం',
            'बैकअप और पुनर्स्थापना परीक्षण का प्रमाण'
        ],
        'Physical scanner': ['నిజమైన స్కానర్', 'वास्तविक स्कैनर'],
        'Physical labels and printer': ['లేబుళ్లు మరియు ప్రింటర్', 'लेबल और प्रिंटर'],
        'Billing and invoice printing': [
            'బిల్లింగ్ మరియు బిల్లు ప్రింటింగ్',
            'बिलिंग और बिल प्रिंटिंग'
        ],
        'Two-counter stock protection': [
            'రెండు కౌంటర్ల స్టాక్ రక్షణ',
            'दो काउंटरों पर स्टॉक सुरक्षा'
        ],
        'Interrupted connection': ['ఆగిపోయిన కనెక్షన్', 'बाधित कनेक्शन'],
        'Staff access': ['సిబ్బంది అనుమతులు', 'कर्मचारी अनुमति'],
        'Reports and cash closing': ['నివేదికలు మరియు నగదు ముగింపు', 'रिपोर्ट और नकद मिलान'],
        'Backup and recovery': ['బ్యాకప్ మరియు పునరుద్ధరణ', 'बैकअप और पुनर्स्थापना'],
        'Staff understanding': ['సిబ్బంది అవగాహన', 'कर्मचारियों की समझ'],
        Result: ['ఫలితం', 'परिणाम'],
        'Evidence / issue': ['ఆధారం / సమస్య', 'प्रमाण / समस्या'],
        Pending: ['పెండింగ్', 'बाकी'],
        Passed: ['పాస్ అయింది', 'सफल'],
        Failed: ['విఫలమైంది', 'असफल'],
        'Record pilot sign-off': ['పరీక్ష ఆమోదం నమోదు చేయండి', 'परीक्षण स्वीकृति दर्ज करें'],
        'Export checklist': ['పరీక్షల జాబితా డౌన్‌లోడ్ చేయండి', 'परीक्षण सूची डाउनलोड करें'],
        'Print checklist': ['పరీక్షల జాబితా ప్రింట్ చేయండి', 'परीक्षण सूची प्रिंट करें'],
        'Please complete or correct the highlighted fields before saving.': [
            'సేవ్ చేయడానికి ముందు గుర్తించిన వివరాలను పూర్తి చేయండి లేదా సరిచేయండి.',
            'सहेजने से पहले चिह्नित जानकारी पूरी करें या सुधारें।'
        ],
        'Cash closing saved.': ['నగదు ముగింపు సేవ్ అయింది.', 'नकद मिलान सहेजा गया।'],
        'Passwords do not match.': ['పాస్‌వర్డ్‌లు ఒకేలా లేవు.', 'पासवर्ड मेल नहीं खाते।']
    };
    const index = new Map(
        Object.entries(captions).map(([key, value]) => [key.toLowerCase(), value])
    );
    const originals = new WeakMap();
    let language = 'en';
    let overrides = new Map();
    let activePack = {};
    const packs = new Map();
    let languageRevision = 0;
    function text(value) {
        if (language === 'en') return value;
        const match = value.match(/^(\s*)(.*?)(\s*)$/s);
        const core = match[2].replace(/\s+/g, ' ');
        const suffix = core.match(/(\s*\*|:)$/)?.[0] || '';
        const words = suffix ? core.slice(0, -suffix.length) : core;
        const custom = overrides.get(words.toLowerCase());
        const packed = Object.hasOwn(activePack, words.toLowerCase()) ? activePack[words.toLowerCase()] : null;
        const translated = index.get(words.toLowerCase());
        const result =
            custom ||
            packed ||
            (['te', 'hi'].includes(language) && translated
                ? translated[language === 'te' ? 0 : 1]
                : null);
        if (!result || result === words) return value;
        return match[1] + words + ' (' + result + ')' + suffix + match[3];
    }
    function applyValue(node, attribute = null) {
        const current = attribute ? node.getAttribute(attribute) : node.nodeValue;
        if (!current) return;
        const saved = originals.get(node) || {};
        const key = attribute || 'text';
        const old = saved[key];
        const original = old && current === old.translated ? old.original : current;
        const translated = text(original);
        saved[key] = { original, translated };
        originals.set(node, saved);
        if (translated !== current) {
            if (attribute) node.setAttribute(attribute, translated);
            else node.nodeValue = translated;
        }
    }
    function originalText(element) {
        if (!element) return '';
        const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
        let value = '';
        while (walker.nextNode()) {
            const node = walker.currentNode;
            if (!node.parentElement.closest('input,select,textarea,.sf-field-validation'))
                value += originals.get(node)?.text?.original || node.nodeValue;
        }
        return value;
    }
    function refresh() {
        const selectors =
            'h1,h2,h3,h4,label,legend,summary,button,a.btn,th,dt,p,small,[data-i18n],.sf-nav a,.sf-nav summary,.configuration-menu a,.sf-breadcrumb a,.sf-breadcrumb span,.sf-topbar-actions > a,.sf-profile-panel a';
        for (const element of document.querySelectorAll(selectors)) {
            if (element.closest('[data-no-translate]'))
                continue;
            // Captions often sit inside a span next to an icon.
            const walker = document.createTreeWalker(element, NodeFilter.SHOW_TEXT);
            while (walker.nextNode()) {
                const node = walker.currentNode;
                if (!node.parentElement.closest('[data-no-translate],svg,script,style'))
                    applyValue(node);
            }
        }
        for (const element of document.querySelectorAll(
            'input[placeholder],button[aria-label],select[aria-label]'
        )) {
            if (element.closest('[data-no-translate]')) continue;
            for (const attribute of ['placeholder', 'aria-label']) applyValue(element, attribute);
        }
        // Translate fixed payment choices only, never product/customer selection options.
        for (const option of document.querySelectorAll(
            '#payment-mode option,#refund-mode option,#main-content option[value=""],#sale-customer option[value="0"],option[data-i18n]'
        )) {
            for (const node of option.childNodes)
                if (node.nodeType === Node.TEXT_NODE) applyValue(node);
        }
    }
    function fieldError(template, parameters, code = language, useOverrides = true, englishMessage = null) {
        const messages = {
            required: ['{label} తప్పనిసరి.', '{label} आवश्यक है।'],
            phone: [
                '{label}: ఖాళీలు లేదా దేశం కోడ్ లేకుండా 10 అంకెలు నమోదు చేయండి.',
                '{label}: बिना खाली जगह या देश कोड के 10 अंक दर्ज करें।'
            ],
            maximum: [
                '{label}: గరిష్ఠంగా {limit} అక్షరాలు మాత్రమే.',
                '{label}: अधिकतम {limit} अक्षर दर्ज करें।'
            ],
            minimum: [
                '{label}: కనీసం {limit} అక్షరాలు నమోదు చేయండి.',
                '{label}: कम से कम {limit} अक्षर दर्ज करें।'
            ],
            email: ['{label}: సరైన ఇమెయిల్ నమోదు చేయండి.', '{label}: सही ईमेल पता दर्ज करें।'],
            lower: ['{label}: కనీస విలువ {limit}.', '{label}: न्यूनतम मान {limit} है।'],
            upper: ['{label}: గరిష్ఠ విలువ {limit}.', '{label}: अधिकतम मान {limit} है।'],
            step: [
                '{label}: {limit} దశల్లో విలువ నమోదు చేయండి.',
                '{label}: {limit} के अंतर में मान दर्ज करें।'
            ],
            format: ['{label}: సరైన రూపంలో నమోదు చేయండి.', '{label}: सही प्रारूप में दर्ज करें।']
        };
        const custom = useOverrides ?
            (overrides.get('validation.' + template) || activePack['validation.' + template]) : null;
        if (!custom && (!['te', 'hi'].includes(code) || !messages[template])) return null;
        let result = custom || messages[template][code === 'te' ? 0 : 1];
        for (const [key, value] of Object.entries(parameters)) {
            const labelKey = String(value).toLowerCase();
            const localized = key === 'label' && useOverrides && Object.hasOwn(activePack, labelKey)
                ? activePack[labelKey] : value;
            result = result.replaceAll('{' + key + '}', String(localized));
        }
        return englishMessage ? englishMessage + ' (' + result + ')' : result;
    }
    function defaults(code) {
        if (!['te', 'hi'].includes(code)) return {};
        // Caption lookup ignores case. Offer only one editable row per caption.
        const uniqueCaptions = new Map();
        for (const [english, values] of Object.entries(captions))
            uniqueCaptions.set(english.toLowerCase(), [english, values[code === 'te' ? 0 : 1]]);
        const result = Object.fromEntries(uniqueCaptions.values());
        for (const kind of [
            'required',
            'phone',
            'maximum',
            'minimum',
            'email',
            'lower',
            'upper',
            'step',
            'format'
        ])
            result['validation.' + kind] = fieldError(
                kind,
                { label: '{label}', limit: '{limit}' },
                code,
                false
            );
        return result;
    }
    async function setLanguage(code) {
        const revision = ++languageRevision;
        let pack = {};
        if (code && code !== 'en') {
            if (!packs.has(code)) {
                const request = fetch('/languages/' + encodeURIComponent(code) + '.json').then(async (response) => {
                    if (!response.ok) throw new Error('Could not load the selected language. Try again.');
                    return response.json();
                }).catch((error) => { packs.delete(code); throw error; });
                packs.set(code, request);
            }
            pack = await packs.get(code);
        }
        // A slow response for an older selection must not replace a newer choice.
        if (revision !== languageRevision) return;
        activePack = pack;
        language = code || 'en';
        overrides = new Map();
        refresh();
        const forms = new Set([...document.querySelectorAll('.is-invalid')]
            .map((input) => input.form).filter(Boolean));
        for (const form of forms) {
            if(form.dataset.validationOwner==='product-edit')window.ProductRegistration?.refreshEdit();
            else if(form.dataset.validationOwner==='product-registration')window.ProductRegistrationForm?.refreshValidation();
            else window.StockFlowValidation?.validate(form, false);
        }
        document.dispatchEvent(new CustomEvent('stockflow:language-ready', { detail: { language } }));
    }
    let loading = false;
    async function loadLanguage() {
        if (loading || !document.querySelector('meta[name="csrf-token"]')) return;
        loading = true;
        const revision = languageRevision;
        try {
            const response = await fetch('/api/screenlanguages');
            if (!response.ok) throw new Error('Language settings could not be loaded.');
            const settings = await response.json();
            if (revision !== languageRevision) return;
            await setLanguage(settings.find((item) => item.screen === 'application')?.language || 'en');
        } catch (error) {
            console.warn(error.message + ' Existing captions remain available.');
        } finally {
            loading = false;
        }
    }
    document.addEventListener('DOMContentLoaded', () => {
        refresh();
        let queued = false;
        new MutationObserver(() => {
            if (queued) return;
            queued = true;
            queueMicrotask(() => {
                queued = false;
                refresh();
            });
        }).observe(document.body, { childList: true, characterData: true, subtree: true });
        loadLanguage();
        window.addEventListener('focus', loadLanguage);
        window.addEventListener('storage', (event) => {
            if (event.key === 'stockflow-language-changed') loadLanguage();
        });
        // Other devices also receive changes while an existing screen is open.
        setInterval(() => { if (!document.hidden) loadLanguage(); }, 30000);
    });
    return { text, fieldError, refresh, defaults, setLanguage, originalText };
})();
