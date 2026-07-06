// Row expand/collapse is client-side only (a CSS class toggle), but the expense list is
// re-rendered wholesale on every SSE push (same on-connect-render pattern as Lists). Without
// tracking + reapplying which rows were expanded, an SSE swap silently collapses whatever the
// user had open — track expanded row ids and restore the class after each swap settles.
(function () {
    const expandedIds = new Set();

    document.addEventListener('click', (e) => {
        const header = e.target.closest('.expense-row-header');
        if (!header) return;
        const wrap = header.closest('.expense-row-wrap');
        if (!wrap) return;
        wrap.classList.toggle('expanded');
        if (wrap.classList.contains('expanded')) expandedIds.add(wrap.id);
        else expandedIds.delete(wrap.id);
    });

    document.body.addEventListener('htmx:afterSettle', () => {
        document.querySelectorAll('.expense-row-wrap').forEach((el) => {
            if (expandedIds.has(el.id)) el.classList.add('expanded');
        });
    });
})();
