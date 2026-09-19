// Dark mode is free for everyone as of 2026-08-16. This file used to carry an _isPremium flag
// and refuse to switch without it, which was never a real gate: the flag lived in the browser
// and anyone could set it from the console. Themes are a preference, not an entitlement.
window.themeManager = {

    init: function () {
        var theme = localStorage.getItem('theme') || 'light';
        document.documentElement.setAttribute('data-theme', theme);
    },

    toggle: function () {
        var current = document.documentElement.getAttribute('data-theme');
        var next = current === 'dark' ? 'light' : 'dark';
        this.set(next);
        return next;
    },

    set: function (theme) {
        document.documentElement.setAttribute('data-theme', theme);
        localStorage.setItem('theme', theme);
    },

    get: function () {
        return document.documentElement.getAttribute('data-theme') || 'light';
    }
};

themeManager.init();
