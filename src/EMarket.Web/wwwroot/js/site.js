// No inline scripts are allowed by the Content-Security-Policy (NFR-35), so every behaviour is wired here with data attributes.
(function () {
    'use strict';

    // Messages appear as toasts.
    document.querySelectorAll('.toast').forEach(function (el) { new bootstrap.Toast(el).show(); });

    // data-confirm asks before a risky action (cancel, delete, block).
    document.addEventListener('submit', function (event) {
        var message = event.target.getAttribute('data-confirm');
        if (message && !window.confirm(message)) { event.preventDefault(); }
    });

    // data-autosubmit submits the form when a select or a quantity box changes.
    document.addEventListener('change', function (event) {
        if (event.target.hasAttribute('data-autosubmit') && event.target.form) { event.target.form.requestSubmit(); }
    });

    // data-back goes to the previous page (the Go back button of the error pages).
    document.addEventListener('click', function (event) {
        if (event.target.closest('[data-back]')) { event.preventDefault(); history.back(); }
    });

    // data-print opens the print dialog (packing slip).
    document.addEventListener('click', function (event) {
        if (event.target.closest('[data-print]')) { window.print(); }
    });

    // Cart count and unread badge refresh within 30 seconds without a reload (FR-14.2).
    var badges = document.querySelectorAll('[data-count]');
    function refreshCounts() {
        if (!badges.length || document.hidden) { return; }
        fetch('/ui/counts', { credentials: 'same-origin', headers: { Accept: 'application/json' } })
            .then(function (r) { return r.ok ? r.json() : null; })
            .then(function (counts) {
                if (!counts) { return; }
                badges.forEach(function (badge) {
                    var value = counts[badge.getAttribute('data-count')] || 0;
                    badge.textContent = value > 99 ? '99+' : value;
                    badge.hidden = value === 0;
                    badge.setAttribute('aria-label', value + ' new');
                });
            })
            .catch(function () { /* the badge keeps its last value */ });
    }
    if (document.querySelector('[data-count="unread"]') || document.querySelector('[data-count="cart"]')) {
        refreshCounts();
        setInterval(refreshCounts, 30000);
    }

    // Product page: choosing a variant updates the price, the stock text and the hidden variant id (FR-5.8).
    var picker = document.getElementById('variant-picker');
    if (picker) {
        var variants = JSON.parse(picker.getAttribute('data-variants') || '[]');
        var apply = function () {
            var chosen = variants.find(function (v) { return v.id === picker.value; });
            var price = document.getElementById('variant-price');
            var stock = document.getElementById('variant-stock');
            var add = document.getElementById('add-to-cart');
            var buy = document.getElementById('buy-now');
            if (!chosen) { if (add) { add.disabled = true; } if (buy) { buy.disabled = true; } return; }
            if (price) { price.textContent = 'Tk ' + chosen.finalPrice.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 }); }
            if (stock) { stock.textContent = chosen.stock > 0 ? (chosen.stock <= 5 ? 'Only ' + chosen.stock + ' left' : 'In stock') : 'Out of stock'; }
            [add, buy].forEach(function (button) { if (button) { button.disabled = chosen.stock <= 0; } });
        };
        picker.addEventListener('change', apply);
        apply();
    }
})();
