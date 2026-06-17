function selectCatalogItem(el) {
    document.getElementById('item-name-input').value = el.dataset.name;
    document.getElementById('category-select').value = el.dataset.categoryId;
    document.getElementById('catalog-item-id').value = el.dataset.catalogId;
    document.getElementById('item-suggestions').innerHTML = '';
}

function showItemDetail(el) {
    document.getElementById('detail-name').textContent = el.dataset.name;
    const emoji = el.dataset.categoryEmoji ?? '';
    const cat = el.dataset.category ?? '';
    document.getElementById('detail-category').textContent = emoji ? emoji + ' ' + cat : cat;
    document.getElementById('detail-list-name').textContent = el.dataset.listName ?? '';
    document.getElementById('detail-added-by').textContent = el.dataset.addedBy ?? '';
    document.getElementById('item-detail-dialog').showModal();
}

document.addEventListener('click', (e) => {
    if (!e.target.closest('#item-name-input') && !e.target.closest('#item-suggestions')) {
        const el = document.getElementById('item-suggestions');
        if (el) el.innerHTML = '';
    }
});
