const { JSDOM } = require('jsdom');
const fs = require('fs');
const path = require('path');
const { bind } = require('./extract-bookmarklet');

const FIX = path.join(__dirname, 'fixtures');
const URL = 'https://www.amazon.com/your-orders/order-details?orderID=113-9957140-8010655&ref_=fed_veo';

function scrape(fixtureFile) {
    const html = fs.readFileSync(path.join(FIX, fixtureFile), 'utf8');
    const dom = new JSDOM(html, { url: URL });
    const bm = bind(dom);
    return { items: bm.findItemBlocks(), orderId: bm.extractOrderId() };
}

let pass = 0, fail = 0;
function eq(name, got, want) {
    const g = JSON.stringify(got), w = JSON.stringify(want);
    if (g === w) { pass++; console.log('  ok   ' + name); }
    else { fail++; console.log('  FAIL ' + name + '\n         got:  ' + g + '\n         want: ' + w); }
}

const cases = [
    ['live-2026-09-08 cart flyout: only the real order item', 'live-2026-09-08-cart-flyout.html',
        [{ title: 'API POND 5 IN 1 POND TEST STRIPS Pond Water Test Strips 25-Count', price: 14.98, quantity: 1 }]],
    ['qty-3: reads quantity 3, per-unit price', 'qty-3.html',
        [{ title: 'Celestial Seasonings Wild Berry Zinger Herbal Tea, Caffeine Free, 20 Tea Bags Box', price: 3.48, quantity: 3 }]],
    ['two-item: both items, correct prices', 'two-item.html',
        [{ title: 'Levoit Core 300-P Air Purifier Filter', price: 25.99, quantity: 1 },
         { title: 'Pure Encapsulations B12 Folate, 60 Capsules', price: 22.50, quantity: 1 }]],
    ['single-item: quantity defaults to 1', 'single-item.html',
        [{ title: 'Pure Encapsulations Vitamin D3 125 mcg (5,000 IU), 60 Capsules', price: 21.00, quantity: 1 }]],
    ['no #orderDetails: returns []', 'no-orderdetails.html', []],
];

for (const [name, file, want] of cases) eq(name, scrape(file).items, want);
eq('order id parsed from the url', scrape('single-item.html').orderId, '113-9957140-8010655');

console.log('\n' + pass + ' passed, ' + fail + ' failed');
process.exit(fail ? 1 : 0);
