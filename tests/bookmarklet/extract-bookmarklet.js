// Pulls the bookmarklet's readable source out of docs/amazon-order-scraper-bookmarklet.md
// (the ```javascript fenced block) and returns findItemBlocks / extractOrderId / parsePrice
// bound to the current global `document` / `window`. Single source of truth - the test never
// keeps its own copy of the scraper logic.
const fs = require('fs');
const path = require('path');

const DOC = path.join(__dirname, '..', '..', 'docs', 'amazon-order-scraper-bookmarklet.md');

function readableSource() {
    const md = fs.readFileSync(DOC, 'utf8');
    const m = md.match(/```javascript\n([\s\S]*?)\n```/);
    if (!m) throw new Error('could not find the ```javascript block in ' + DOC);
    return m[1];
}

// The doc's block is a full IIFE ending in navigator.clipboard/alert calls. Strip the outer
// `(function () { ... })();` wrapper and the trailing driver so we can call the functions
// directly, then re-expose them.
function loadFunctions() {
    const src = readableSource();
    const body = src
        .replace(/^\(function \(\) \{\s*\n\s*'use strict';\s*\n/, '')
        .replace(/\n\s*const orderId = extractOrderId\(\);[\s\S]*$/, '\n');
    // eslint-disable-next-line no-new-func
    const factory = new Function('window', 'document', 'URLSearchParams',
        body + '\nreturn { extractOrderId, parsePrice, findItemBlocks };');
    return factory;
}

const factory = loadFunctions();

function bind(dom) {
    return factory(dom.window, dom.window.document, dom.window.URLSearchParams);
}

module.exports = { bind, readableSource };
