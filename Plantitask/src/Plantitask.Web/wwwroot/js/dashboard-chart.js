window.dashboardChart = {

    _chart: null,

    render: function (canvasId, labels, data) {
        const canvas = document.getElementById(canvasId);
        if (!canvas) return;

        if (this._chart) {
            this._chart.destroy();
            this._chart = null;
        }

        const ctx = canvas.getContext('2d');

        this._chart = new Chart(ctx, {
            type: 'line',
            data: {
                labels: labels,
                datasets: [{
                    label: 'Completed',
                    data: data,
                    borderColor: '#9CC103',
                    backgroundColor: 'rgba(156, 193, 3, 0.08)',
                    borderWidth: 2,
                    pointBackgroundColor: '#9CC103',
                    pointBorderColor: '#ffffff',
                    pointBorderWidth: 2,
                    pointRadius: 3,
                    pointHoverRadius: 5,
                    fill: true,
                    tension: 0.3
                }]
            },
            options: {
                responsive: true,
                maintainAspectRatio: false,
                plugins: {
                    legend: { display: false },
                    tooltip: {
                        backgroundColor: 'rgba(255,255,255,0.95)',
                        titleColor: '#424242',
                        bodyColor: '#9CC103',
                        borderColor: '#F0F0F0',
                        borderWidth: 1,
                        padding: 10,
                        callbacks: {
                            label: ctx => `${ctx.parsed.y} completed`
                        }
                    }
                },
                scales: {
                    x: {
                        grid: { display: false },
                        ticks: {
                            color: '#BDBDBD',
                            font: { family: 'DM Sans', size: 11 },
                            maxRotation: 0,
                            callback: function (val, index, ticks) {
                                if (index === 0 || index === 14 || index === ticks.length - 1)
                                    return this.getLabelForValue(val);
                                return '';
                            }
                        }
                    },
                    y: {
                        beginAtZero: true,
                        grid: { color: '#F0F0F0' },
                        ticks: {
                            color: '#BDBDBD',
                            font: { family: 'DM Sans', size: 11 },
                            precision: 0
                        }
                    }
                }
            }
        });
    },

    destroy: function () {
        if (this._chart) {
            this._chart.destroy();
            this._chart = null;
        }
    }
};