// Row expand/collapse is a client-side class toggle; site.js keeps the class across SSE morphs.
window.householdApp ??= {};

if (!window.householdApp.expensesEventsBound) {
    document.addEventListener('click', (e) => {
        const header = e.target.closest('.expense-row-header');
        if (!header) return;
        header.closest('.expense-row-wrap')?.classList.toggle('expanded');
    });

    // Upcoming open/closed is remembered per device; site.js carries the classes across SSE morphs.
    const upcomingKey = 'expenses.upcoming.open';
    // Held in memory too so a blocked localStorage keeps the strip open across SSE morphs.
    let open = false;
    try { open = localStorage.getItem(upcomingKey) === '1'; } catch { }
    const applyUpcoming = () => {
        const strip = document.getElementById('upcoming');
        if (!strip) return;
        strip.classList.toggle('expanded', open);
        strip.querySelector('.upcoming-toggle')?.setAttribute('aria-expanded', String(open));
    };

    document.addEventListener('click', (e) => {
        const toggle = e.target.closest('.upcoming-toggle');
        if (toggle) {
            open = !open;
            applyUpcoming();
            try { localStorage.setItem(upcomingKey, open ? '1' : '0'); } catch { }
            return;
        }
        const more = e.target.closest('.upcoming-more');
        if (more) more.closest('.upcoming').classList.add('show-all');
    });
    document.addEventListener('DOMContentLoaded', applyUpcoming);
    document.addEventListener('htmx:afterSettle', applyUpcoming);
    applyUpcoming();

    window.householdApp.expensesEventsBound = true;
}
