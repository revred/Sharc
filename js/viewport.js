// JS-based arena timer — runs on browser event loop, updates DOM directly.
// Zero contention with .NET sync context (critical for single-threaded WASM).
window.arenaTimer = {
    _id: null,
    _start: 0,

    start: function () {
        this.stop();
        this._start = performance.now();
        var self = this;
        this._id = setInterval(function () {
            var el = document.querySelector('.arena-timer');
            if (!el) return;
            var secs = ((performance.now() - self._start) / 1000).toFixed(1);
            // Update the last text node (the "3.7s" part, after the blink-dot span)
            for (var i = el.childNodes.length - 1; i >= 0; i--) {
                if (el.childNodes[i].nodeType === 3) {
                    el.childNodes[i].textContent = '\n                ' + secs + 's';
                    break;
                }
            }
        }, 100);
    },

    stop: function () {
        if (this._id) {
            clearInterval(this._id);
            this._id = null;
        }
    },

    elapsed: function () {
        return performance.now() - this._start;
    }
};

// Viewport detection helpers for Arena
window.viewportHelper = {
    getWidth: function () {
        return window.innerWidth;
    },

    observePages: function (dotNetRef) {
        const sections = document.querySelectorAll('.arena-page');
        if (!sections.length) return;

        const observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
                if (entry.isIntersecting) {
                    const id = entry.target.id;
                    if (id && dotNetRef) {
                        dotNetRef.invokeMethodAsync('OnPageVisible', id);
                    }
                }
            });
        }, { threshold: 0.3 });

        sections.forEach(function (section) {
            observer.observe(section);
        });
    },

    scrollToElement: function (id) {
        var el = document.getElementById(id);
        if (el) {
            el.scrollIntoView({ behavior: 'smooth', block: 'start' });
        }
    }
};
