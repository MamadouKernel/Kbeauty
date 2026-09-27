window.kekeMotion = (() => {
    const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)');
    const revealSelector = [
        'main section', 'main article', '.premium-card', '.salon-card',
        '.partner-shop-card', '.plan-card', '.summary-row', '.flow-alert',
        '.request-card', '.staff-appointment', '.admin-content > :not(script)'
    ].join(',');
    const staggerSelector = '.salon-grid, .request-grid, .list-group, .review-gallery, .commission-list, main .grid';
    let observer;
    let scanQueued = false;

    function createObserver() {
        observer?.disconnect();
        if (reduceMotion.matches || !('IntersectionObserver' in window)) return null;
        observer = new IntersectionObserver(entries => {
            for (const entry of entries) {
                if (!entry.isIntersecting) continue;
                entry.target.classList.add('is-visible');
                observer.unobserve(entry.target);
            }
        }, { rootMargin: '0px 0px -7% 0px', threshold: 0.08 });
        return observer;
    }

    function shouldSkip(element) {
        return element.closest('.leaflet-container, .capture-frame, .scanner-viewport, .reconnect-modal, .toast-stack') !== null;
    }

    function scan(root = document) {
        if (reduceMotion.matches) return;
        if (!observer) createObserver();

        root.querySelectorAll(staggerSelector).forEach(group => {
            Array.from(group.children).slice(0, 12).forEach((child, index) => {
                child.style.setProperty('--kb-stagger', `${Math.min(index, 8) * 45}ms`);
            });
        });

        root.querySelectorAll(revealSelector).forEach(element => {
            if (element.classList.contains('kb-reveal') || shouldSkip(element)) return;
            element.classList.add('kb-reveal');
            observer?.observe(element);
        });

        requestAnimationFrame(() => {
            document.querySelectorAll('.kb-reveal').forEach(element => {
                const rect = element.getBoundingClientRect();
                if (rect.top < window.innerHeight * 0.96 && rect.bottom > 0) element.classList.add('is-visible');
            });
        });
    }

    function queueScan() {
        if (scanQueued) return;
        scanQueued = true;
        requestAnimationFrame(() => {
            scanQueued = false;
            scan();
        });
    }

    function animatePage() {
        if (reduceMotion.matches) return;
        document.body.classList.remove('kb-page-enter');
        requestAnimationFrame(() => document.body.classList.add('kb-page-enter'));
        queueScan();
    }

    function pressFeedback(event) {
        if (reduceMotion.matches) return;
        const target = event.target.closest('button, a');
        if (!target || target.closest('.leaflet-container')) return;
        const rect = target.getBoundingClientRect();
        target.style.setProperty('--kb-press-x', `${event.clientX - rect.left}px`);
        target.style.setProperty('--kb-press-y', `${event.clientY - rect.top}px`);
        target.classList.remove('kb-pressed');
        void target.offsetWidth;
        target.classList.add('kb-pressed');
        window.setTimeout(() => target.classList.remove('kb-pressed'), 420);
    }

    function init() {
        document.documentElement.classList.toggle('kb-reduced-motion', reduceMotion.matches);
        document.documentElement.classList.add('kb-motion-ready');
        createObserver();
        scan();
        animatePage();

        document.addEventListener('pointerdown', pressFeedback, { passive: true });
        document.addEventListener('enhancedload', animatePage);
        new MutationObserver(queueScan).observe(document.body, { childList: true, subtree: true });
        reduceMotion.addEventListener('change', () => {
            document.documentElement.classList.toggle('kb-reduced-motion', reduceMotion.matches);
            if (reduceMotion.matches) {
                observer?.disconnect();
                document.querySelectorAll('.kb-reveal').forEach(element => element.classList.add('is-visible'));
            } else {
                createObserver();
                queueScan();
            }
        });
    }

    if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init, { once: true });
    else init();

    return { scan, animatePage };
})();
