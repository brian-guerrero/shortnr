/*
 * PRD-019 link-detail modal.
 *
 * The modal markup is fetched via HTMX into #link-detail with the chart JSON
 * embedded in <script id="link-detail-chart-data" type="application/json">, so
 * the charts build against DSG-002 tokens without a second round trip. Close
 * via the X, the backdrop, or Esc; body scroll locks while the modal is open.
 */
(function () {
    'use strict';

    var region = document.getElementById('link-detail');
    if (!region) return;

    document.body.addEventListener('htmx:afterSwap', function (e) {
        if (e.detail.target.id !== 'link-detail') return;
        renderCharts();
        lockScroll(true);
        var closeBtn = region.querySelector('[data-modal-close]');
        if (closeBtn) closeBtn.focus();
    });

    function close() {
        region.innerHTML = '';
        lockScroll(false);
    }

    function lockScroll(lock) {
        document.body.style.overflow = lock ? 'hidden' : '';
    }

    // Event delegation over the region so backdrop/X/Esc work long after the
    // swap that created them, and across re-fetches.
    region.addEventListener('click', function (e) {
        if (e.target.closest('[data-modal-close]') || e.target.closest('[data-modal-overlay]') === e.target) {
            close();
        }
    });

    document.addEventListener('keydown', function (e) {
        if (e.key === 'Escape' && region.childElementCount > 0) close();
    });

    function renderCharts() {
        var dataEl = region.querySelector('#link-detail-chart-data');
        if (!dataEl || typeof Chart === 'undefined') return;
        var data;
        try { data = JSON.parse(dataEl.textContent); } catch (err) { return; }

        var TOKEN = function (name, fallback) {
            return getComputedStyle(document.documentElement).getPropertyValue(name).trim() || fallback;
        };
        var ink = TOKEN('--sn-ink', '#111111');
        var paper = TOKEN('--sn-paper', '#faf9f5');
        var accent = TOKEN('--sn-accent', '#ffd23f');
        var violet = TOKEN('--sn-accent-secondary', '#7c5cff');
        Chart.defaults.font.family = TOKEN('--sn-font-body', 'system-ui, sans-serif');
        Chart.defaults.color = ink;

        var timelineCanvas = region.querySelector('#detail-timeline');
        if (timelineCanvas) {
            new Chart(timelineCanvas, {
                type: 'bar',
                data: {
                    labels: data.timeline.map(function (d) { return d.label; }),
                    datasets: [{
                        label: 'Clicks',
                        data: data.timeline.map(function (d) { return d.count; }),
                        backgroundColor: accent,
                        borderColor: ink,
                        borderWidth: 2
                    }]
                },
                options: {
                    responsive: true,
                    plugins: { legend: { display: false } },
                    scales: {
                        x: { ticks: { maxTicksLimit: 8 } },
                        y: { beginAtZero: true, ticks: { precision: 0 } }
                    }
                }
            });
        }

        var devicesCanvas = region.querySelector('#detail-devices');
        if (devicesCanvas && data.devices.length > 0) {
            var series = [
                accent, violet,
                TOKEN('--sn-status-info', '#6ec1ff'),
                TOKEN('--sn-status-success', '#3ddc84'),
                TOKEN('--sn-mid-gray', '#8a8578')
            ];
            new Chart(devicesCanvas, {
                type: 'doughnut',
                data: {
                    labels: data.devices.map(function (d) { return d.name; }),
                    datasets: [{
                        data: data.devices.map(function (d) { return d.count; }),
                        backgroundColor: data.devices.map(function (_, i) { return series[i % series.length]; }),
                        borderColor: ink,
                        borderWidth: 2
                    }]
                },
                options: {
                    responsive: true,
                    plugins: { legend: { position: 'right' } }
                }
            });
        }
    }
})();