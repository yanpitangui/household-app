function selectCatalogItem(el) {
    const isMobile = !!el.closest('#item-suggestions-mobile');
    if (isMobile) {
        document.getElementById('item-name-input-mobile').value = el.dataset.name;
        const catSel = document.getElementById('category-select-mobile');
        if (catSel) { catSel.value = el.dataset.categoryId || ''; refreshAppSelect(catSel); }
        document.getElementById('catalog-item-id-mobile').value = el.dataset.catalogId;
        document.getElementById('item-suggestions-mobile').innerHTML = '';
        document.getElementById('add-item-btn-mobile').click();
    } else {
        document.getElementById('item-name-input').value = el.dataset.name;
        const catId = el.dataset.categoryId || '';
        const catItem = document.querySelector(`.desktop-cat-item[data-id="${CSS.escape(catId)}"]`);
        if (catItem) selectDesktopCategory(catItem); else resetDesktopCategory();
        document.getElementById('catalog-item-id').value = el.dataset.catalogId;
        document.getElementById('item-suggestions').innerHTML = '';
        htmx.trigger(document.getElementById('add-item-form'), 'submit');
    }
}

function selectDesktopCategory(btn) {
    document.querySelectorAll('.desktop-cat-item').forEach(b => {
        b.classList.remove('selected');
        const chk = b.querySelector('.desktop-cat-check');
        if (chk) chk.style.display = 'none';
    });
    btn.classList.add('selected');
    const chk = btn.querySelector('.desktop-cat-check');
    if (chk) chk.style.display = '';
    const catBtn = document.getElementById('desktop-cat-btn');
    if (catBtn) catBtn.textContent = btn.dataset.emoji || '🏷️';
    const sel = document.getElementById('category-select-desktop');
    if (sel) sel.value = btn.dataset.id || '';
}

function resetDesktopCategory() {
    const noneBtn = document.querySelector('.desktop-cat-item[data-id=""]');
    if (noneBtn) selectDesktopCategory(noneBtn);
}

let _drawerSelectController = null;

function openAddItemDrawer() {
    if (window.innerWidth >= 640) return;
    const drawer = document.getElementById('add-item-drawer');
    if (!drawer) return;
    drawer.showModal();
    setTimeout(() => document.getElementById('item-name-input-mobile')?.focus(), 80);

    _drawerSelectController = new AbortController();
    const catSel = document.getElementById('category-select-mobile');
    if (catSel) {
        const { signal } = _drawerSelectController;
        catSel.addEventListener('focus', () => drawer.classList.add('lifted'), { signal });
        catSel.addEventListener('blur', () => drawer.classList.remove('lifted'), { signal });
    }
}

function closeAddItemDrawer() {
    if (_drawerSelectController) { _drawerSelectController.abort(); _drawerSelectController = null; }
    const drawer = document.getElementById('add-item-drawer');
    if (!drawer) return;
    drawer.classList.remove('lifted');
    drawer.close();
    const form = document.getElementById('add-item-form-mobile');
    if (form) form.reset();
    const suggestions = document.getElementById('item-suggestions-mobile');
    if (suggestions) suggestions.innerHTML = '';
    const catalogId = document.getElementById('catalog-item-id-mobile');
    if (catalogId) catalogId.value = '';
}

function rebuildCategoryChips() {
    const select = document.getElementById('category-select');
    if (!select) return;

    const mobileSelect = document.getElementById('category-select-mobile');
    if (mobileSelect) { mobileSelect.innerHTML = select.innerHTML; refreshAppSelect(mobileSelect); }

    const desktopSelect = document.getElementById('category-select-desktop');
    if (desktopSelect) desktopSelect.innerHTML = select.innerHTML;

    const detailSelect = document.getElementById('detail-category-select');
    if (detailSelect) { detailSelect.innerHTML = select.innerHTML; refreshAppSelect(detailSelect); }

    const menu = document.querySelector('.desktop-cat-menu');
    if (menu) {
        menu.innerHTML = '';
        Array.from(select.options).forEach(opt => {
            const li = document.createElement('li');
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'desktop-cat-item';
            btn.dataset.id = opt.value;
            const text = opt.textContent.trim();
            const emoji = text.match(/^\p{Emoji}/u)?.[0] || '🏷️';
            const name = text.replace(/^\p{Emoji}\s*/u, '').trim() || text;
            btn.dataset.emoji = emoji;
            btn.innerHTML = `<span class="desktop-cat-emoji">${emoji}</span><span>${name}</span><span class="desktop-cat-check" style="display:none">✓</span>`;
            li.appendChild(btn);
            menu.appendChild(li);
        });
        resetDesktopCategory();
    }
}

function showItemDetail(el) {
    document.getElementById('detail-name').textContent = el.dataset.name;
    document.getElementById('detail-item-id').value = el.dataset.itemId ?? '';
    document.getElementById('detail-list-id').value = el.dataset.listId ?? '';
    document.getElementById('detail-list-name').textContent = el.dataset.listName ?? '';
    document.getElementById('detail-added-by').textContent = el.dataset.addedBy ?? '';

    const catSelect = document.getElementById('detail-category-select');
    catSelect.value = el.dataset.categoryId ?? '';
    refreshAppSelect(catSelect);

    document.getElementById('item-detail-dialog').showModal();
}

window.householdApp ??= {};

if (!window.householdApp.listsEventsBound) {
    document.addEventListener('click', (e) => {
        const catItem = e.target.closest('.desktop-cat-item');
        if (catItem) {
            selectDesktopCategory(catItem);
            const toggle = document.getElementById('desktop-cat-btn');
            if (toggle) bootstrap.Dropdown.getOrCreateInstance(toggle).hide();
        }

        if (e.target.id === 'add-item-drawer') {
            closeAddItemDrawer();
        }

        if (e.target.id === 'item-detail-dialog') {
            e.target.close();
        }

        if (!e.target.closest('#item-name-input') && !e.target.closest('#item-suggestions-wrap')) {
            const el = document.getElementById('item-suggestions');
            if (el) el.innerHTML = '';
        }
    });

    window.householdApp.listsEventsBound = true;
}
