/*
 * PRD-019 bulk link actions on the dashboard.
 *
 * The checkboxes live in the search-results table (swapped by HTMX on every
 * search/filter); the toolbar sits above it and stays in sync client-side.
 * The forms submit the selected ids with hx-include="[name='selected']:checked"
 * and the result (refreshed table + OOB toast) swaps in server-side.
 */
(function () {
    'use strict';

    function rows(root) {
        return Array.prototype.slice.call((root || document).querySelectorAll('[data-select-row]'));
    }

    function selectedCount() {
        return document.querySelectorAll('[name="selected"]:checked').length;
    }

    function updateToolbar() {
        var toolbar = document.querySelector('[data-bulk-toolbar]');
        if (!toolbar) return;
        var count = selectedCount();
        var countEl = toolbar.querySelector('[data-bulk-count]');
        if (countEl) countEl.textContent = count === 1 ? '1 selected' : count + ' selected';
        toolbar.classList.toggle('is-active', count > 0);
        var selectAll = toolbar.querySelector('[data-select-all]') || document.querySelector('[data-select-all]');
        if (selectAll) {
            var boxes = rows(document.querySelector('#search-results'));
            selectAll.checked = boxes.length > 0 && boxes.every(function (b) { return b.checked; });
            selectAll.indeterminate = !selectAll.checked && boxes.some(function (b) { return b.checked; });
        }
    }

    function toggleSelectAll(checked) {
        rows(document.querySelector('#search-results')).forEach(function (box) {
            box.checked = checked;
        });
        updateToolbar();
    }

    document.addEventListener('change', function (e) {
        if (e.target && (e.target.matches('[data-select-row]') || e.target.matches('[data-select-all]'))) {
            if (e.target.matches('[data-select-all]')) toggleSelectAll(e.target.checked);
            updateToolbar();
        }
    });

    document.addEventListener('click', function (e) {
        var clear = e.target && e.target.closest('[data-bulk-clear]');
        if (clear) {
            toggleSelectAll(false);
            clear.blur();
        }
    });

    // After every swap (search, filter, a bulk action's refreshed table), re-sync
    // the toolbar/select-all states so stale counts never linger.
    document.body.addEventListener('htmx:afterSwap', updateToolbar);
    document.body.addEventListener('htmx:oobAfterSwap', updateToolbar);

    updateToolbar();
})();