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
