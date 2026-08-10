/*
 * Mobile nav toggle.
 *
 * The button starts `hidden` in markup; CSS also keeps the link list fully
 * visible until the `js` class lands on <html> (set by the inline script in
 * _Layout.cshtml's <head>). Together that means a phone with JavaScript
 * disabled still gets today's wrapped, always-open link list rather than a
 * menu stuck behind a button nothing can open.
 */
(function () {
    'use strict';

    var toggle = document.querySelector('[data-nav-toggle]');
    var nav = toggle && document.getElementById(toggle.getAttribute('aria-controls'));
    var links = nav && nav.querySelector('.nav-links');
    if (!toggle || !links) return;

    toggle.hidden = false;

    function isOpen() {
        return links.classList.contains('is-open');
    }

    function setOpen(open) {
        links.classList.toggle('is-open', open);
        toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        toggle.setAttribute('aria-label', open ? 'Close navigation menu' : 'Toggle navigation menu');
        toggle.textContent = open ? '✕' : '☰';
    }

    toggle.addEventListener('click', function () {
        setOpen(!isOpen());
    });

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && isOpen()) {
            setOpen(false);
            toggle.focus();
        }
    });

    // A resize back past the breakpoint (rotating a tablet, or a desktop
    // window shrinking then growing) should not leave the drawer stuck open
    // once the toggle itself disappears. 768px matches the .site-nav
    // breakpoint in site.css.
    var media = window.matchMedia('(max-width: 768px)');
    function onBreakpointChange(e) {
        if (!e.matches) setOpen(false);
    }
    if (media.addEventListener) {
        media.addEventListener('change', onBreakpointChange);
    } else if (media.addListener) {
        media.addListener(onBreakpointChange); // Safari < 14
    }
})();
