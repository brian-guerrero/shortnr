/*
 * Colour-scheme toggle.
 *
 * The <html data-color-scheme> attribute is set by a tiny inline script in
 * _Layout.cshtml's <head> so the page never paints in the wrong scheme;
 * this file only wires up the control and keeps the choice in sync.
 *
 * No stored preference means "follow the OS", including when the OS setting
 * changes mid-session. Clicking the toggle pins an explicit choice.
 */
(function () {
    'use strict';

    var STORAGE_KEY = 'sn-color-scheme';
    var root = document.documentElement;

    function storedScheme() {
        try {
            return localStorage.getItem(STORAGE_KEY);
        } catch (e) {
            return null; // private mode, storage disabled — fall back to the OS
        }
    }

    function systemScheme() {
        return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }

    function syncToggles(scheme) {
        var isDark = scheme === 'dark';
        var toggles = document.querySelectorAll('[data-scheme-toggle]');
        for (var i = 0; i < toggles.length; i++) {
            toggles[i].hidden = false;
            toggles[i].setAttribute('aria-pressed', isDark ? 'true' : 'false');
            toggles[i].setAttribute('aria-label', isDark ? 'Switch to light theme' : 'Switch to dark theme');
            toggles[i].textContent = isDark ? '☀' : '☾';
        }
    }

    function apply(scheme) {
        root.setAttribute('data-color-scheme', scheme);
        syncToggles(scheme);
        // Chart.js bakes colours in at construction time, so anything holding
        // a rendered canvas needs a chance to rebuild against the new tokens.
        document.body.dispatchEvent(new CustomEvent('sn:color-scheme-change', {
            detail: { scheme: scheme }
        }));
    }

    syncToggles(root.getAttribute('data-color-scheme') || systemScheme());

    document.addEventListener('click', function (e) {
        // Delegated, so the toggle survives any partial swap that re-renders it.
        var toggle = e.target instanceof Element && e.target.closest('[data-scheme-toggle]');
        if (!toggle) return;
        var next = root.getAttribute('data-color-scheme') === 'dark' ? 'light' : 'dark';
        try {
            localStorage.setItem(STORAGE_KEY, next);
        } catch (err) {
            // Not persisting is survivable; the toggle still works this session.
        }
        apply(next);
    });

    var media = window.matchMedia('(prefers-color-scheme: dark)');
    var onSystemChange = function () {
        if (storedScheme()) return; // an explicit choice outranks the OS
        apply(systemScheme());
    };
    if (media.addEventListener) {
        media.addEventListener('change', onSystemChange);
    } else if (media.addListener) {
        media.addListener(onSystemChange); // Safari < 14
    }
})();
