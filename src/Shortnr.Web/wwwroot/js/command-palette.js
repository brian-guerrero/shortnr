/*
 * PRD-019 command palette (Cmd+K / Ctrl+K).
 *
 * v1 scope: navigate, create link, switch workspace, toggle dark mode, switch
 * theme. The list is rendered by the server (Shared/_CommandPalette) so nav
 * visibility and the workspace/theme options always match the rest of the
 * site; this file only handles open/close, panel navigation (root vs. the
 * "themes" sub-panel), full-text filtering over data-palette-command labels
 * scoped to the active panel, and keyboard-first execution (↑/↓/↵, Esc, Cmd+K).
 *
 * Panels: each data-palette-panel="<name>" element is a self-contained list;
 * exactly one is visible at a time. "Switch theme" (data-palette-open-panel)
 * opens the "themes" panel without closing the whole command palette; "Back"
 * (data-palette-back) and Esc return to "root". Filtering, arrow-key
 * navigation, and Enter-to-select all operate only over items in the
 * currently active panel — an item hidden by its ancestor panel is never a
 * match, even if its label matches the query.
 */
(function () {
    'use strict';

    var palette = document.querySelector('[data-palette]');
    if (!palette) return;

    var input = palette.querySelector('[data-palette-input]');
    var empty = palette.querySelector('[data-palette-empty]');
    var panels = Array.prototype.slice.call(palette.querySelectorAll('[data-palette-panel]'));
    var items = Array.prototype.slice.call(palette.querySelectorAll('[data-palette-command]'));

    function activePanel() {
        return panels.filter(function (p) { return !p.hidden; })[0] || panels[0];
    }

    function itemsInPanel(panel) {
        return items.filter(function (item) { return panel.contains(item); });
    }

    function visibleItems() {
        return itemsInPanel(activePanel()).filter(function (item) { return !item.hidden; });
    }

    function openPanel(name) {
        panels.forEach(function (p) { p.hidden = p.getAttribute('data-palette-panel') !== name; });
        input.value = '';
        filterItems('');
        input.focus();
        highlight(visibleItems()[0] || null);
    }

    function open() {
        palette.hidden = false;
        document.body.classList.add('palette-open');
        document.body.scrollTop = 0;
        openPanel('root');
        window.setTimeout(function () { input.focus(); }, 0);
    }

    function close() {
        palette.hidden = true;
        document.body.classList.remove('palette-open');
        // Return focus to the opener so keyboard users stay in place.
        var opener = document.querySelector('[data-palette-opener]');
        if (opener) opener.focus();
    }

    function filterItems(query) {
        var q = query.trim().toLowerCase();
        var any = false;
        itemsInPanel(activePanel()).forEach(function (item) {
            var label = (item.getAttribute('data-palette-command') || item.textContent).toLowerCase();
            var show = !q || label.indexOf(q) !== -1;
            item.hidden = !show;
            if (show) any = true;
        });
        empty.hidden = any;
    }

    function highlight(item) {
        items.forEach(function (i) { i.classList.remove('is-highlighted'); });
        if (item) {
            item.classList.add('is-highlighted');
            if (item.scrollIntoView) item.scrollIntoView({ block: 'nearest' });
        }
    }

    function move(delta) {
        var list = visibleItems();
        if (list.length === 0) return;
        var idx = list.indexOf(document.querySelector('.is-highlighted'));
        var next = list[(idx + delta + list.length) % list.length];
        highlight(next);
    }

    function select(item) {
        if (!item) return;
        if (item.matches('a')) {
            window.location.href = item.getAttribute('href');
        } else if (item.hasAttribute('data-palette-open-panel')) {
            openPanel(item.getAttribute('data-palette-open-panel'));
            return; // Drilling into a panel keeps the palette open.
        } else if (item.hasAttribute('data-palette-back')) {
            openPanel('root');
            return;
        } else if (item.matches('form')) {
            item.submit();
        } else if (item.closest('form')) {
            // The palette's form items highlight their inner submit <button>
            // (so styling/aria stays on the clickable control), but submitting
            // must target the <form> itself — a bare button click here wouldn't
            // trigger the palette's Enter-key path.
            item.closest('form').submit();
        } else if (item.hasAttribute('data-palette-toggle-scheme')) {
            // Reuse the colour-scheme toggle's own click handler contract:
            // synthesise a click on the real toggle if one is rendered, else
            // fall back to flipping the attribute directly.
            var real = document.querySelector('[data-scheme-toggle]');
            if (real) {
                real.click();
            } else {
                var root = document.documentElement;
                var next = root.getAttribute('data-color-scheme') === 'dark' ? 'light' : 'dark';
                try { localStorage.setItem('sn-color-scheme', next); } catch (e) { }
                root.setAttribute('data-color-scheme', next);
            }
        }
        close();
    }

    function selectHighlighted() {
        select(document.querySelector('.palette-item.is-highlighted, .palette-item-form.is-highlighted .palette-item'));
    }

    // Item click (anchors/buttons) — the palette stays open for forms so the
    // navigation fall-through in select() closes it once the click lands.
    palette.addEventListener('click', function (e) {
        var item = e.target.closest('[data-palette-command]');
        if (!item) return;
        if (item.matches('a')) { /* let the anchor navigate */ }
        else if (item.matches('form')) { return; /* form's own submit button handles it */ }
        else {
            // Buttons (toggle-scheme, open-panel, back) have no native
            // browser action, so run the same logic Enter would.
            select(item);
            e.preventDefault();
        }
    });

    document.addEventListener('keydown', function (e) {
        var isOpen = !palette.hidden;
        var meta = e.metaKey || e.ctrlKey;
        if (!isOpen && meta && (e.key === 'k' || e.key === 'K')) {
            e.preventDefault();
            open();
            return;
        }
        if (!isOpen) return;
        if (e.key === 'Escape') {
            e.preventDefault();
            // One level of back-navigation before closing outright, so
            // drilling into "themes" and hitting Esc doesn't lose the palette.
            if (activePanel().getAttribute('data-palette-panel') !== 'root') {
                openPanel('root');
            } else {
                close();
            }
        } else if (e.key === 'ArrowDown') {
            e.preventDefault();
            move(1);
        } else if (e.key === 'ArrowUp') {
            e.preventDefault();
            move(-1);
        } else if (e.key === 'Enter') {
            e.preventDefault();
            selectHighlighted();
        } else if (e.key === 'Tab') {
            /* Keep default focus behaviour; nothing to trap yet. */
        }
    });

    document.addEventListener('click', function (e) {
        if (!palette.hidden && e.target.closest('[data-palette-close]')) close();
    });

    input.addEventListener('input', function () {
        filterItems(input.value);
        highlight(visibleItems()[0] || null);
    });
})();
