window.themeManager = {
    _isPremium: false,

    init: function () {
        var theme = localStorage.getItem('theme') || 'light';
        if (!this._isPremium && theme === 'dark') {
            theme = 'light';
            localStorage.setItem('theme', 'light');
        }
        document.documentElement.setAttribute('data-theme', theme);
    },

    toggle: function () {
        if (!this._isPremium) {
            console.warn('Dark mode requires premium.');
            return 'light';
        }
        var current = document.documentElement.getAttribute('data-theme');
        var next = current === 'dark' ? 'light' : 'dark';
        document.documentElement.setAttribute('data-theme', next);
        localStorage.setItem('theme', next);
        return next;
    },

    set: function (theme) {
        if (theme === 'dark' && !this._isPremium) {
            console.warn('Dark mode requires premium.');
            return;
        }
        document.documentElement.setAttribute('data-theme', theme);
        localStorage.setItem('theme', theme);
    },

    get: function () {
        return document.documentElement.getAttribute('data-theme') || 'light';
    },

    setPremiumStatus: function (isPremium) {
        this._isPremium = isPremium;
        if (!isPremium && this.get() === 'dark') {
            this.set_force('light');
        }
    },

    set_force: function (theme) {
        document.documentElement.setAttribute('data-theme', theme);
        localStorage.setItem('theme', theme);
    }
};

themeManager.init();
