window.themeManager = {
    init: function () {
        var theme = localStorage.getItem('theme') || 'light';
        document.documentElement.setAttribute('data-theme', theme);
    },
    toggle: function () {
        var current = document.documentElement.getAttribute('data-theme');
        var next = current === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        localStorage.setItem('theme', next);
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