

window.fieldEngine = {

    app: null,
    dotnetRef: null,
    initialized: false,

    // ---- World / viewport ----
    fieldWidth: 1400,
    fieldHeight: 800,
    viewportW: 0,
    viewportH: 0,
    worldScale: 1,
    containerEl: null,

    // ---- Camera (pan offset in screen pixels) ----
    camera: null,
    cameraX: 0,
    cameraY: 0,

    // ---- Scene layers ----
    terrainLayer: null,
    overlayLayer: null,
    treeLayer: null,
    uiLayer: null,

    // ---- State ----
    userSeed: 12345,

    // Tree dragging
    dragging: null,      // { groupId, container, sprite, label, size }
    dragOffset: { x: 0, y: 0 },
    wasDragged: false,

    // Seed planting
    seedGhost: null,
    isDraggingSeed: false,
    // Handlers stored so they can be removed on cancel/destroy
    _seedPlaceHandler: null,
    _seedCancelHandler: null,

    // Touch panning
    isPanning: false,
    panTouchId: null,
    lastTouchX: 0,
    lastTouchY: 0,

    // ---- Terrain ----
    tileVariants: [
        'images/field/grass-tile-large.png',
        'images/field/grass-tile-large-variant1.png',
        'images/field/grass-tile-large-variant2.png',
        'images/field/grass-tile-large-variant3.png',
        'images/field/grass-tile-large-variant4.png',
    ],
    displayTileSize: 16,

    // ---- Tree assets ----
    // Keyed by TreeStage enum int value from C#.
    // Stage 0 (EmptySoil) and 6 (FloweringTree) share the same
    // asset intentionally — adjust if you add unique art later
    treeAssets: {
        0: 'images/field/tree-flowering.png',
        1: 'images/field/tree-sprout-seed.png',
        2: 'images/field/tree-sprout-seed.png',
        3: 'images/field/tree-sapling.png',
        4: 'images/field/young-tree.png',
        5: 'images/field/tree.png',
        6: 'images/field/tree-flowering.png',
    },

    // All sizes are the same today. This map is kept explicit so
    // per-stage sizes can be differentiated without a refactor
    treeSizes: { 0: 160, 1: 160, 2: 160, 3: 160, 4: 160, 5: 160, 6: 160 },

    // Trees collection — Map<groupId string, treeObj>
    trees: new Map(),

    // ---- Bound event handler refs (stored for proper cleanup) ----
    _boundTouchStart: null,
    _boundTouchMove: null,
    _boundTouchEnd: null,
    _boundResize: null,


    init: async function (containerId, dotnetRef, width, height, userSeed) {
        if (this.initialized) {
            this.destroy();
        }

        this.dotnetRef = dotnetRef;
        this.fieldWidth = width;
        this.fieldHeight = height;
        this.userSeed = userSeed || 12345;
        this.cameraX = 0;
        this.cameraY = 0;

        const container = document.getElementById(containerId);
        if (!container) {
            console.error('[Field] Container not found:', containerId);
            return;
        }
        this.containerEl = container;

        this.viewportW = container.clientWidth || window.innerWidth;
        this.viewportH = container.clientHeight || window.innerHeight;
        this._calcScale();

        try {
            this.app = new PIXI.Application();
            await this.app.init({
                width: this.viewportW,
                height: this.viewportH,
                antialias: false,
                backgroundColor: 0x7EB356,
                resolution: 1,
                autoDensity: false,
                resizeTo: null,
            });

            // Nearest-neighbour scaling keeps pixel-art tiles crisp
            PIXI.TextureStyle.defaultOptions.scaleMode = 'nearest';

            container.innerHTML = '';
            container.appendChild(this.app.canvas);

            const cs = this.app.canvas.style;
            cs.width = '100%';
            cs.height = '100%';
            cs.display = 'block';
            cs.touchAction = 'none';   // prevents browser scroll hijacking on touch

            this._buildSceneGraph();

            await this._generateTerrain();

            this._attachPointerEvents();
            this._attachTouchEvents();
            this._attachResizeEvent();

            this._clampCamera();
            this._applyCamera();

            this.initialized = true;
            console.log('[Field] Ready');
        } catch (err) {
            console.error('[Field] Init failed:', err);
        }
    },




    _buildSceneGraph: function () {
        this.camera = new PIXI.Container();
        this.terrainLayer = new PIXI.Container();
        this.overlayLayer = new PIXI.Container();
        this.treeLayer = new PIXI.Container();
        this.uiLayer = new PIXI.Container();

        this.treeLayer.sortableChildren = true;

        this.camera.addChild(this.terrainLayer);
        this.camera.addChild(this.overlayLayer);
        this.camera.addChild(this.treeLayer);

        this.app.stage.addChild(this.camera);
        this.app.stage.addChild(this.uiLayer);

        this.camera.scale.set(this.worldScale);

        const overlay = new PIXI.Graphics();
        overlay.rect(0, 0, this.fieldWidth, this.fieldHeight);
        overlay.fill({ color: 0x9CC103, alpha: 0.35 });
        this.overlayLayer.addChild(overlay);
    },



    _attachPointerEvents: function () {
        this.app.stage.eventMode = 'static';
        this.app.stage.hitArea = new PIXI.Rectangle(0, 0, this.viewportW, this.viewportH);
        this.app.stage.on('pointermove', (e) => this._onPointerMove(e));
        this.app.stage.on('pointerup', (e) => this._onPointerUp(e));
        this.app.stage.on('pointerupoutside', (e) => this._onPointerUp(e));
    },

    _attachTouchEvents: function () {
        this._boundTouchStart = (e) => this._onTouchStart(e);
        this._boundTouchMove = (e) => this._onTouchMove(e);
        this._boundTouchEnd = (e) => this._onTouchEnd(e);

        this.app.canvas.addEventListener('touchstart', this._boundTouchStart, { passive: false });
        this.app.canvas.addEventListener('touchmove', this._boundTouchMove, { passive: false });
        this.app.canvas.addEventListener('touchend', this._boundTouchEnd, { passive: true });
        this.app.canvas.addEventListener('touchcancel', this._boundTouchEnd, { passive: true });
    },

    _attachResizeEvent: function () {
        this._boundResize = () => this._onResize();
        window.addEventListener('resize', this._boundResize);
    },



    _calcScale: function () {
        const sx = this.viewportW / this.fieldWidth;
        const sy = this.viewportH / this.fieldHeight;
        this.worldScale = Math.min(1, Math.max(sx, sy));
    },

    _clampCamera: function () {
        const scaledW = this.fieldWidth * this.worldScale;
        const scaledH = this.fieldHeight * this.worldScale;
        const maxX = Math.max(0, scaledW - this.viewportW);
        const maxY = Math.max(0, scaledH - this.viewportH);

        this.cameraX = Math.max(0, Math.min(maxX, this.cameraX));
        this.cameraY = Math.max(0, Math.min(maxY, this.cameraY));
    },

    _applyCamera: function () {
        this.camera.x = -this.cameraX;
        this.camera.y = -this.cameraY;
    },

    _screenToWorld: function ({ x, y }) {
        return {
            x: (x + this.cameraX) / this.worldScale,
            y: (y + this.cameraY) / this.worldScale,
        };
    },

    focusTree: function (groupId) {
        const tree = this.trees.get(groupId);
        if (!tree) return;

        this.cameraX = (tree.container.x * this.worldScale) - (this.viewportW / 2);
        this.cameraY = (tree.container.y * this.worldScale) - (this.viewportH / 2);
        this._clampCamera();
        this._applyCamera();
    },



    _onResize: function () {
        if (!this.app || !this.containerEl) return;

        this.viewportW = this.containerEl.clientWidth || window.innerWidth;
        this.viewportH = this.containerEl.clientHeight || window.innerHeight;

        this.app.renderer.resize(this.viewportW, this.viewportH);
        this.app.stage.hitArea = new PIXI.Rectangle(0, 0, this.viewportW, this.viewportH);

        this._calcScale();
        this.camera.scale.set(this.worldScale);
        this._clampCamera();
        this._applyCamera();
    },



    _createRng: function (seed) {
        let s = Math.abs(seed) || 12345;
        return () => {
            s = (s * 16807) % 2147483647;
            return (s - 1) / 2147483646;
        };
    },



    _generateTerrain: async function () {
        try {
            const textures = [];
            for (const path of this.tileVariants) {
                try {
                    const tex = await PIXI.Assets.load(path);
                    tex.source.scaleMode = 'nearest';
                    textures.push(tex);
                } catch {
                    console.warn('[Field] Tile failed to load:', path);
                }
            }

            if (textures.length === 0) {
                console.error('[Field] No terrain tiles loaded — field will be blank');
                return;
            }

            const rng = this._createRng(this.userSeed);
            const tilesX = Math.ceil(this.fieldWidth / this.displayTileSize);
            const tilesY = Math.ceil(this.fieldHeight / this.displayTileSize);

            for (let y = 0; y < tilesY; y++) {
                for (let x = 0; x < tilesX; x++) {
                    const idx = Math.floor(rng() * textures.length);
                    const tile = new PIXI.Sprite(textures[idx]);
                    tile.x = x * this.displayTileSize;
                    tile.y = y * this.displayTileSize;
                    tile.width = tile.height = this.displayTileSize;
                    this.terrainLayer.addChild(tile);
                }
            }

            console.log(`[Field] Terrain ${tilesX}x${tilesY} tiles`);
        } catch (err) {
            console.error('[Field] Terrain generation failed:', err);
        }
    },



    loadTrees: async function (treeDataJson) {
        for (const [, tree] of this.trees) {
            tree.container.parent?.removeChild(tree.container);
        }
        this.trees.clear();

        const data = JSON.parse(treeDataJson);
        for (const item of data) {
            await this._createTree(item);
        }

        console.log(`[Field] Loaded ${this.trees.size} tree(s)`);
    },

    _createTree: async function (data) {
        const assetPath = this.treeAssets[data.currentTreeStage];
        if (!assetPath) return;

        const size = this.treeSizes[data.currentTreeStage] ?? 80;

        try {
            const texture = await PIXI.Assets.load(assetPath);
            texture.source.scaleMode = 'nearest';

            const container = new PIXI.Container();
            container.x = data.x;
            container.y = data.y;
            container.eventMode = 'static';
            container.cursor = 'pointer';
            container.zIndex = Math.floor(data.y);

            const sprite = new PIXI.Sprite(texture);
            sprite.anchor.set(0.5, 1);
            sprite.width = sprite.height = size;

            const label = new PIXI.Text({
                text: data.groupName,
                style: {
                    fontFamily: 'DM Sans, Arial',
                    fontSize: 13,
                    fontWeight: '700',
                    fill: 0xFFFFFF,
                },
            });
            label.anchor.set(0.5, 1);
            label.y = -10;
            label.alpha = 0;

            container.addChild(sprite);
            container.addChild(label);

            const treeObj = { groupId: data.groupId, container, sprite, label, size };
            this._attachTreeEvents(treeObj, data);

            this.trees.set(data.groupId, treeObj);
            this.treeLayer.addChild(container);
        } catch (err) {
            console.error('[Field] Failed to create tree:', data.groupId, err);
        }
    },

    _attachTreeEvents: function (treeObj, data) {
        const { container, label } = treeObj;

        container.on('pointerenter', () => {
            if (this.dragging === treeObj || this.isDraggingSeed) return;
            label.alpha = 1;
            container.scale.set(1.06);
        });

        container.on('pointerleave', () => {
            if (this.dragging === treeObj) return;
            label.alpha = 0;
            container.scale.set(1);
        });

        container.on('pointertap', () => {
            if (!this.wasDragged && !this.isDraggingSeed) {
                this.dotnetRef.invokeMethodAsync('OnTreeClicked', data.groupId);
            }
        });

        container.on('pointerdown', (e) => {
            if (this.isDraggingSeed) return;
            e.stopPropagation();

            this.dragging = treeObj;
            this.wasDragged = false;

            const worldPos = this._screenToWorld(e.global);
            this.dragOffset.x = worldPos.x - container.x;
            this.dragOffset.y = worldPos.y - container.y;

            container.scale.set(1.1);
            container.alpha = 0.8;
            container.zIndex = 10000;
        });
    },

    updateTree: async function (groupId, newStage, _completionPercentage) {
        const tree = this.trees.get(groupId);
        if (!tree) return;

        const assetPath = this.treeAssets[newStage];
        if (!assetPath) return;

        const newSize = this.treeSizes[newStage] ?? 80;

        try {
            const texture = await PIXI.Assets.load(assetPath);
            texture.source.scaleMode = 'nearest';

            tree.sprite.texture = texture;
            tree.sprite.width = newSize;
            tree.sprite.height = newSize;


            tree.container.scale.set(1.2);
            setTimeout(() => { if (tree.container) tree.container.scale.set(1); }, 300);
        } catch (err) {
            console.warn('[Field] Tree update failed:', err);
        }
    },

    addTreeLive: async function (treeDataJson) {
        const data = JSON.parse(treeDataJson);
        if (!this.trees.has(data.groupId)) {
            await this._createTree(data);
        }
    },

    _onPointerMove: function (e) {
        if (this.isDraggingSeed && this.seedGhost) {
            this.seedGhost.x = e.global.x;
            this.seedGhost.y = e.global.y;
            return;
        }

        if (!this.dragging) return;

        this.wasDragged = true;

        const worldPos = this._screenToWorld(e.global);
        const margin = 60;
        const c = this.dragging.container;

        c.x = Math.max(margin, Math.min(this.fieldWidth - margin, worldPos.x - this.dragOffset.x));
        c.y = Math.max(margin, Math.min(this.fieldHeight - margin, worldPos.y - this.dragOffset.y));
        c.zIndex = Math.floor(c.y) + 10000;
    },

    _onPointerUp: function () {
        if (!this.dragging) return;

        const tree = this.dragging;
        tree.container.scale.set(1);
        tree.container.alpha = 1;
        tree.container.zIndex = Math.floor(tree.container.y);

        if (this.wasDragged) {
            this.dotnetRef.invokeMethodAsync('OnTreeMoved', tree.groupId, tree.container.x, tree.container.y);
        }

        this.dragging = null;
    },



    _onTouchStart: function (e) {
        if (this.dragging || this.isDraggingSeed) return;
        if (e.touches.length !== 1) return;

        const touch = e.touches[0];
        const rect = this.app.canvas.getBoundingClientRect();
        const sx = (touch.clientX - rect.left) * (this.viewportW / rect.width);
        const sy = (touch.clientY - rect.top) * (this.viewportH / rect.height);
        const wp = this._screenToWorld({ x: sx, y: sy });

        for (const [, tree] of this.trees) {
            const c = tree.container;
            const half = tree.size / 2;
            if (wp.x >= c.x - half && wp.x <= c.x + half &&
                wp.y >= c.y - tree.size && wp.y <= c.y) {
                return;
            }
        }

        e.preventDefault();
        this.isPanning = true;
        this.panTouchId = touch.identifier;
        this.lastTouchX = touch.clientX;
        this.lastTouchY = touch.clientY;
    },

    _onTouchMove: function (e) {
        if (!this.isPanning) return;

        let touch = null;
        for (let i = 0; i < e.touches.length; i++) {
            if (e.touches[i].identifier === this.panTouchId) {
                touch = e.touches[i];
                break;
            }
        }
        if (!touch) return;

        e.preventDefault();

        this.cameraX -= touch.clientX - this.lastTouchX;
        this.cameraY -= touch.clientY - this.lastTouchY;

        this._clampCamera();
        this._applyCamera();

        this.lastTouchX = touch.clientX;
        this.lastTouchY = touch.clientY;
    },

    _onTouchEnd: function (e) {
        if (!this.isPanning) return;

        const stillDown = Array.from(e.touches).some(t => t.identifier === this.panTouchId);
        if (!stillDown) {
            this.isPanning = false;
            this.panTouchId = null;
        }
    },



    startSeedDrag: async function () {
        this.isDraggingSeed = true;
        this.app.stage.cursor = 'none';

        try {
            const texture = await PIXI.Assets.load('images/field/tree-sprout-seed.png');
            texture.source.scaleMode = 'nearest';

            this.seedGhost = new PIXI.Sprite(texture);
            this.seedGhost.anchor.set(0.5, 0.5);
            this.seedGhost.width = 64;
            this.seedGhost.height = 64;
            this.seedGhost.alpha = 0.8;
            this.seedGhost.eventMode = 'none';
            this.uiLayer.addChild(this.seedGhost);
        } catch (err) {
            console.warn('[Field] Seed sprite failed:', err);
            this._cancelSeedDrag();
            return;
        }

        this._seedPlaceHandler = (e) => {
            if (!this.isDraggingSeed) return;
            const worldPos = this._screenToWorld(e.global);
            this._cleanupSeedHandlers();
            this._cancelSeedDrag();
            this.dotnetRef.invokeMethodAsync('OnPlantSeed', worldPos.x, worldPos.y);
        };

        this._seedCancelHandler = (e) => {
            if (e.button === 2 || e.key === 'Escape') {
                this._cleanupSeedHandlers();
                this._cancelSeedDrag();
                this.dotnetRef.invokeMethodAsync('OnSeedCancelled');
            }
        };

        this.app.stage.on('pointertap', this._seedPlaceHandler);
        document.addEventListener('keydown', this._seedCancelHandler);
    },

    _cleanupSeedHandlers: function () {
        if (this._seedPlaceHandler) {
            this.app.stage.off('pointertap', this._seedPlaceHandler);
            this._seedPlaceHandler = null;
        }
        if (this._seedCancelHandler) {
            document.removeEventListener('keydown', this._seedCancelHandler);
            this._seedCancelHandler = null;
        }
    },

    _cancelSeedDrag: function () {
        this.isDraggingSeed = false;
        if (this.app) this.app.stage.cursor = 'default';
        if (this.seedGhost?.parent) {
            this.seedGhost.parent.removeChild(this.seedGhost);
        }
        this.seedGhost = null;
    },

    cancelPlanting: function () {
        this._cleanupSeedHandlers();
        this._cancelSeedDrag();
    },



    destroy: function () {
        this._cleanupSeedHandlers();
        this._cancelSeedDrag();

        if (this.app?.canvas) {
            this.app.canvas.removeEventListener('touchstart', this._boundTouchStart);
            this.app.canvas.removeEventListener('touchmove', this._boundTouchMove);
            this.app.canvas.removeEventListener('touchend', this._boundTouchEnd);
            this.app.canvas.removeEventListener('touchcancel', this._boundTouchEnd);
        }

        if (this._boundResize) {
            window.removeEventListener('resize', this._boundResize);
            this._boundResize = null;
        }

        if (this.app) {
            try { this.app.destroy(true, { children: true }); } catch { /* ignore */ }
            this.app = null;
        }

        this.trees.clear();
        this.camera = null;
        this.terrainLayer = null;
        this.overlayLayer = null;
        this.treeLayer = null;
        this.uiLayer = null;
        this.containerEl = null;
        this.dotnetRef = null;
        this.initialized = false;
        this.isPanning = false;
        this.dragging = null;

        console.log('[Field] Destroyed');
    },
};