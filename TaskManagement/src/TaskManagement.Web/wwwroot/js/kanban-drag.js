window.kanbanTree = {
    observe: function (dotnetRef) {
        document.addEventListener('animationend', function (e) {
            if (e.target.classList.contains('kanban-tree-entering')) {
                dotnetRef.invokeMethodAsync('OnTreeAnimationEnd');
            }
        });
    }
};

window.kanbanDrag = (() => {
    let _dotnetRef = null;
    let _dragging = null;       
    let _columns = [];         
    let _dropIndicator = null;
    let _scrollInterval = null;

    function init(dotnetRef) {
        _dotnetRef = dotnetRef;
        document.addEventListener('mousedown', onMouseDown, { passive: false });
        document.addEventListener('mousemove', onMouseMove, { passive: false });
        document.addEventListener('mouseup', onMouseUp);
        document.addEventListener('touchstart', onTouchStart, { passive: false });
        document.addEventListener('touchmove', onTouchMove, { passive: false });
        document.addEventListener('touchend', onTouchEnd);
        _createDropIndicator();
    }

    function destroy() {
        document.removeEventListener('mousedown', onMouseDown);
        document.removeEventListener('mousemove', onMouseMove);
        document.removeEventListener('mouseup', onMouseUp);
        document.removeEventListener('touchstart', onTouchStart);
        document.removeEventListener('touchmove', onTouchMove);
        document.removeEventListener('touchend', onTouchEnd);
        _removeDropIndicator();
        _dotnetRef = null;
        cancelAutoScroll();
    }

    // Drop Indicator

    function _createDropIndicator() {
        _dropIndicator = document.createElement('div');
        _dropIndicator.className = 'kanban-drop-indicator';
        _dropIndicator.style.display = 'none';
        document.body.appendChild(_dropIndicator);
    }

    function _removeDropIndicator() {
        if (_dropIndicator) {
            _dropIndicator.remove();
            _dropIndicator = null;
        }
    }

    // Mouse Events

    function onMouseDown(e) {
        const card = e.target.closest('.kanban-card');
        if (!card || e.button !== 0) return;

        // Don't drag if clicking interactive elements
        if (e.target.closest('button, a, input, select, textarea, .mud-select')) return;

        e.preventDefault();
        startDrag(card, e.clientX, e.clientY);
    }

    function onMouseMove(e) {
        if (!_dragging) return;
        e.preventDefault();
        moveDrag(e.clientX, e.clientY);
    }

    function onMouseUp(e) {
        if (!_dragging) return;
        endDrag(e.clientX, e.clientY);
    }

    // Touch Events

    function onTouchStart(e) {
        const card = e.target.closest('.kanban-card');
        if (!card) return;
        if (e.target.closest('button, a, input, select, textarea')) return;

        const touch = e.touches[0];
        startDrag(card, touch.clientX, touch.clientY);
    }

    function onTouchMove(e) {
        if (!_dragging) return;
        e.preventDefault();
        const touch = e.touches[0];
        moveDrag(touch.clientX, touch.clientY);
    }

    function onTouchEnd(e) {
        if (!_dragging) return;
        const touch = e.changedTouches[0];
        endDrag(touch.clientX, touch.clientY);
    }

    // Core Drag Logic

    function startDrag(card, x, y) {
        const taskId = card.dataset.taskId;
        const statusId = card.dataset.statusId;
        if (!taskId || !statusId) return;

        const rect = card.getBoundingClientRect();

        // Create ghost
        const ghost = card.cloneNode(true);
        ghost.className = 'kanban-card kanban-card-ghost';
        ghost.style.position = 'fixed';
        ghost.style.width = rect.width + 'px';
        ghost.style.zIndex = '9999';
        ghost.style.pointerEvents = 'none';
        ghost.style.transform = 'rotate(3deg) scale(1.02)';
        ghost.style.opacity = '0.9';
        ghost.style.boxShadow = '0 8px 32px rgba(0,0,0,0.18)';
        ghost.style.transition = 'none';
        document.body.appendChild(ghost);

        _dragging = {
            el: card,
            taskId,
            sourceStatusId: parseInt(statusId),
            ghost,
            offsetX: x - rect.left,
            offsetY: y - rect.top,
            startX: x,
            startY: y,
            moved: false
        };

        positionGhost(x, y);
        cacheColumnRects();
    }

    function moveDrag(x, y) {
        if (!_dragging) return;

        // Only start visual drag after 4px movement (prevents accidental drags on click)
        if (!_dragging.moved) {
            const dx = Math.abs(x - _dragging.startX);
            const dy = Math.abs(y - _dragging.startY);
            if (dx < 4 && dy < 4) return;
            _dragging.moved = true;
            _dragging.el.classList.add('kanban-card-dragging');
        }

        positionGhost(x, y);
        updateDropIndicator(x, y);
        handleAutoScroll(x, y);
    }

    function endDrag(x, y) {
        if (!_dragging) return;

        cancelAutoScroll();
        _dropIndicator.style.display = 'none';

        const dragging = _dragging;
        _dragging = null;

        // Clean up
        dragging.el.classList.remove('kanban-card-dragging');
        dragging.ghost.remove();

        if (!dragging.moved) return; // Was just a click, not a drag

        // Find drop target
        const drop = getDropTarget(x, y);
        if (!drop) return;

        // Don't fire if dropped in same position
        if (drop.statusId === dragging.sourceStatusId && drop.index === getCardIndex(dragging.el)) return;

        // Callback to Blazor
        if (_dotnetRef) {
            _dotnetRef.invokeMethodAsync('OnCardDropped', dragging.taskId, drop.statusId, drop.index);
        }
    }

    function positionGhost(x, y) {
        if (!_dragging || !_dragging.ghost) return;
        _dragging.ghost.style.left = (x - _dragging.offsetX) + 'px';
        _dragging.ghost.style.top = (y - _dragging.offsetY) + 'px';
    }

    // Column & Card Position Tracking

    function cacheColumnRects() {
        _columns = [];
        document.querySelectorAll('.kanban-column').forEach(col => {
            const body = col.querySelector('.kanban-column-body');
            if (!body) return;
            const statusId = parseInt(col.dataset.statusId);
            if (isNaN(statusId)) return;

            const colRect = col.getBoundingClientRect();
            const cards = Array.from(body.querySelectorAll('.kanban-card:not(.kanban-card-ghost)'));
            const cardRects = cards.map(c => ({
                el: c,
                rect: c.getBoundingClientRect()
            }));

            _columns.push({ el: col, body, statusId, rect: colRect, cards: cardRects });
        });
    }

    function getDropTarget(x, y) {
        for (const col of _columns) {
            if (x >= col.rect.left && x <= col.rect.right) {
                // Find insertion index
                let index = col.cards.length;
                for (let i = 0; i < col.cards.length; i++) {
                    const cr = col.cards[i].rect;
                    const midY = cr.top + cr.height / 2;
                    if (y < midY) {
                        index = i;
                        break;
                    }
                }
                return { statusId: col.statusId, index };
            }
        }
        return null;
    }

    function getCardIndex(cardEl) {
        const body = cardEl.closest('.kanban-column-body');
        if (!body) return -1;
        const cards = Array.from(body.querySelectorAll('.kanban-card:not(.kanban-card-ghost)'));
        return cards.indexOf(cardEl);
    }

    // Drop Indicator

    function updateDropIndicator(x, y) {
        let shown = false;
        for (const col of _columns) {
            if (x >= col.rect.left && x <= col.rect.right) {
                col.el.classList.add('kanban-column-drag-over');

                if (col.cards.length === 0) {
                    // Empty column — show indicator at top of body
                    const bodyRect = col.body.getBoundingClientRect();
                    showIndicator(col.rect.left + 12, bodyRect.top + 8, col.rect.width - 24);
                } else {
                    let placed = false;
                    for (let i = 0; i < col.cards.length; i++) {
                        const cr = col.cards[i].rect;
                        if (y < cr.top + cr.height / 2) {
                            showIndicator(col.rect.left + 12, cr.top - 4, col.rect.width - 24);
                            placed = true;
                            break;
                        }
                    }
                    if (!placed) {
                        const lastRect = col.cards[col.cards.length - 1].rect;
                        showIndicator(col.rect.left + 12, lastRect.bottom + 4, col.rect.width - 24);
                    }
                }
                shown = true;
            } else {
                col.el.classList.remove('kanban-column-drag-over');
            }
        }
        if (!shown) _dropIndicator.style.display = 'none';
    }

    function showIndicator(left, top, width) {
        _dropIndicator.style.display = 'block';
        _dropIndicator.style.left = left + 'px';
        _dropIndicator.style.top = top + 'px';
        _dropIndicator.style.width = width + 'px';
    }

    // Auto-scroll

    function handleAutoScroll(x, y) {
        const wrapper = document.querySelector('.kanban-columns-wrapper');
        if (!wrapper) return;

        cancelAutoScroll();

        const wrapperRect = wrapper.getBoundingClientRect();
        const edgeSize = 60;
        const speed = 8;

        let scrollX = 0;
        if (x < wrapperRect.left + edgeSize) scrollX = -speed;
        else if (x > wrapperRect.right - edgeSize) scrollX = speed;

        let scrollY = 0;
        if (y < wrapperRect.top + edgeSize) scrollY = -speed;
        else if (y > wrapperRect.bottom - edgeSize) scrollY = speed;

        if (scrollX !== 0 || scrollY !== 0) {
            _scrollInterval = setInterval(() => {
                wrapper.scrollLeft += scrollX;
                wrapper.scrollTop += scrollY;
                cacheColumnRects(); // Recache after scroll
            }, 16);
        }
    }

    function cancelAutoScroll() {
        if (_scrollInterval) {
            clearInterval(_scrollInterval);
            _scrollInterval = null;
        }
    }

    return { init, destroy };
})();
