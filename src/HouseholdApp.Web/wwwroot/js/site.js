window.householdApp ??= {};
window.householdApp.appSelectId ??= 0;

function initToasts() {
    document.querySelectorAll('.toast-success').forEach(function (toast) {
        setTimeout(function () {
            toast.style.transition = 'opacity 0.3s';
            toast.style.opacity = '0';
            setTimeout(function () { toast.remove(); }, 300);
        }, 4000);
    });
}

window.copyInviteUrl = function (btn) {
    var url = btn.dataset.inviteUrl;
    navigator.clipboard.writeText(url).then(function () {
        var orig = btn.innerHTML;
        btn.innerHTML = '✓ Copied!';
        setTimeout(function () { btn.innerHTML = orig; }, 1800);
    });
};

window.shareInviteUrl = function (btn) {
    navigator.share({ title: 'Join my household', url: btn.dataset.inviteUrl });
};

document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('.invite-btn-share').forEach(function (btn) {
        if (navigator.canShare && navigator.canShare({ url: btn.dataset.inviteUrl })) {
            btn.style.display = '';
        }
    });
});

window.openAddTaskDrawer = function () {
    var drawer = document.getElementById('add-task-drawer');
    if (!drawer) return;
    drawer.showModal();
    setTimeout(function () {
        document.getElementById('task-name-input-mobile')?.focus();
    }, 80);
};

window.closeAddTaskDrawer = function () {
    var drawer = document.getElementById('add-task-drawer');
    if (!drawer) return;
    drawer.close();
    var form = document.getElementById('add-task-form-mobile');
    if (form) form.reset();
};

document.addEventListener('click', function (e) {
    var drawer = document.getElementById('add-task-drawer');
    if (drawer && e.target === drawer) drawer.close();
});

function initAppSelects(root = document) {
    const selects = root instanceof HTMLSelectElement && root.matches('select.form-select')
        ? [root]
        : Array.from(root.querySelectorAll('select.form-select'));
    selects.forEach(enhanceAppSelect);
}

function enhanceAppSelect(select) {
    if (select.dataset.appSelectEnhanced === 'true') {
        select.classList.add('app-select-native');
        select.setAttribute('aria-hidden', 'true');
        refreshAppSelect(select);
        return;
    }

    const id = `app-select-${++window.householdApp.appSelectId}`;
    const wrapper = document.createElement('div');
    wrapper.className = 'dropdown app-select';
    wrapper.dataset.appSelectFor = id;

    const button = document.createElement('button');
    button.id = `${id}-button`;
    button.className = 'app-select-toggle';
    button.type = 'button';
    button.setAttribute('aria-expanded', 'false');

    const label = document.createElement('span');
    label.className = 'app-select-label';

    const chevron = document.createElement('span');
    chevron.className = 'app-select-chevron';
    chevron.textContent = '▾';

    const menu = document.createElement('ul');
    menu.className = 'dropdown-menu app-select-menu';
    menu.setAttribute('aria-labelledby', button.id);

    button.append(label, chevron);
    wrapper.append(button, menu);
    select.insertAdjacentElement('afterend', wrapper);

    select.dataset.appSelectEnhanced = 'true';
    select.dataset.appSelectId = id;
    select.classList.add('app-select-native');
    select.setAttribute('aria-hidden', 'true');
    select.tabIndex = -1;
    select.addEventListener('change', () => refreshAppSelect(select));
    button.addEventListener('click', () => toggleAppSelect(wrapper));

    refreshAppSelect(select);
}

function closeAppSelect(wrapper) {
    wrapper.classList.remove('show');
    wrapper.querySelector('.app-select-toggle')?.setAttribute('aria-expanded', 'false');
    wrapper.querySelector('.app-select-menu')?.classList.remove('show');
}

function closeOtherAppSelects(current) {
    document.querySelectorAll('.app-select.show').forEach(wrapper => {
        if (wrapper !== current) closeAppSelect(wrapper);
    });
}

function toggleAppSelect(wrapper) {
    const isOpen = wrapper.classList.contains('show');
    closeOtherAppSelects(wrapper);

    if (isOpen) {
        closeAppSelect(wrapper);
        return;
    }

    wrapper.classList.add('show');
    wrapper.querySelector('.app-select-toggle')?.setAttribute('aria-expanded', 'true');
    wrapper.querySelector('.app-select-menu')?.classList.add('show');
}

function refreshAppSelect(select) {
    const id = select.dataset.appSelectId;
    const wrapper = id ? document.querySelector(`[data-app-select-for="${id}"]`) : null;
    if (!wrapper) return;

    const menu = wrapper.querySelector('.app-select-menu');
    const label = wrapper.querySelector('.app-select-label');
    const toggle = wrapper.querySelector('.app-select-toggle');
    const selected = select.selectedOptions[0] ?? select.options[0];

    toggle.disabled = select.disabled;
    label.textContent = selected?.textContent?.trim() || '';
    menu.innerHTML = '';

    Array.from(select.options).forEach(option => {
        const item = document.createElement('li');
        const button = document.createElement('button');
        button.type = 'button';
        button.className = 'dropdown-item app-select-item';
        button.dataset.value = option.value;
        button.textContent = option.textContent.trim();
        button.disabled = option.disabled;
        button.setAttribute('aria-pressed', option.selected ? 'true' : 'false');
        button.addEventListener('click', () => {
            if (option.disabled) return;
            select.value = option.value;
            select.dispatchEvent(new Event('change', { bubbles: true }));
            closeAppSelect(wrapper);
        });
        item.appendChild(button);
        menu.appendChild(item);
    });
}

if (!window.householdApp.siteEventsBound) {
    document.addEventListener('click', (e) => {
        if (!e.target.closest('.app-select')) {
            closeOtherAppSelects(null);
        }
    });

    document.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') closeOtherAppSelects(null);
    });

    document.addEventListener('DOMContentLoaded', () => {
        initAppSelects();
    });

    document.addEventListener('htmx:afterSwap', (e) => {
        const target = (e.detail?.target instanceof Element) ? e.detail.target : document;
        initAppSelects(target);
        document.querySelectorAll('select.form-select[data-app-select-enhanced="true"]')
            .forEach(refreshAppSelect);
    });

    document.addEventListener('htmx:afterSettle', (e) => {
        const target = (e.detail?.target instanceof Element) ? e.detail.target : document;
        initAppSelects(target);
    });

    document.addEventListener('reset', (e) => {
        setTimeout(() => initAppSelects(e.target instanceof Element ? e.target : document));
    });

    window.householdApp.siteEventsBound = true;
}

initAppSelects();
