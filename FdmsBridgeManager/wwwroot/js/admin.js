// Consultancy Admin Portal State Management
let currentUser = null;
let currentTab = 'overview';
let allPlans = [];
let hardwareTierPrices = [];
let allClients = [];
let allSubscriptions = [];
let allUsers = [];

let subState = {
    search: '',
    status: 'All',
    page: 1,
    pageSize: 10,
    totalPages: 1,
    totalItems: 0
};
let subDebounceTimer = null;

let clientState = {
    search: '',
    status: 'All',
    page: 1,
    pageSize: 10,
    totalPages: 1,
    totalItems: 0
};
let clientDebounceTimer = null;

document.addEventListener('DOMContentLoaded', () => {
    setupAuthListeners();
    setupColorPickerSync();
    checkStoredSession();
});

// 1. Session Auth Logic
function setupAuthListeners() {
    const form = document.getElementById('login-form');
    form.addEventListener('submit', async (e) => {
        e.preventDefault();
        const btn = document.getElementById('btn-login');
        btn.disabled = true;
        btn.innerHTML = '<i class="fa-solid fa-circle-notch fa-spin"></i> Authenticating...';

        const payload = {
            username: document.getElementById('login-username').value.trim(),
            password: document.getElementById('login-password').value.trim()
        };

        try {
            const res = await fetch('/api/v1/admin/auth/login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            const data = await res.json();
            btn.disabled = false;
            btn.innerHTML = '<i class="fa-solid fa-right-to-bracket"></i> Login to Console';

            if (!res.ok) {
                alert(data.message || 'Invalid login credentials.');
                return;
            }

            currentUser = data;
            localStorage.setItem('bm_session', JSON.stringify(data));
            initAdminDashboard();
        } catch (err) {
            btn.disabled = false;
            btn.innerHTML = '<i class="fa-solid fa-right-to-bracket"></i> Login to Console';
            alert('Login failed: ' + err.message);
        }
    });
}

function checkStoredSession() {
    const stored = localStorage.getItem('bm_session');
    if (stored) {
        try {
            currentUser = JSON.parse(stored);
            initAdminDashboard();
        } catch {
            localStorage.removeItem('bm_session');
        }
    }
}

function logout() {
    localStorage.removeItem('bm_session');
    currentUser = null;
    document.getElementById('admin-app').classList.add('hidden');
    document.getElementById('login-modal').classList.remove('hidden');
}

function initAdminDashboard() {
    document.getElementById('login-modal').classList.add('hidden');
    document.getElementById('admin-app').classList.remove('hidden');

    document.getElementById('user-fullname').innerText = currentUser.fullName || currentUser.username;
    document.getElementById('user-username').innerText = `@${currentUser.username}`;
    document.getElementById('user-role-badge').innerText = currentUser.role;

    switchTab('overview');
    loadWhiteLabelConfig();
    loadTokenLedgerStatus();
    loadHardwareTierPricing();
}

function getAuthHeaders(extraHeaders = {}) {
    const headers = { ...extraHeaders };
    if (currentUser && currentUser.token) {
        headers['X-Session-Token'] = currentUser.token;
        headers['Authorization'] = `Bearer ${currentUser.token}`;
    }
    return headers;
}

function hasPermission(permission) {
    if (!currentUser || !currentUser.permissions) return false;
    if (currentUser.role === 'SuperAdmin' || currentUser.permissions.includes('SuperAdmin') || currentUser.permissions.includes('*')) return true;
    return currentUser.permissions.includes(permission);
}

function updateTabPermissionAlerts() {
    document.getElementById('plans-permission-alert')?.classList.toggle('hidden', hasPermission('ManagePlans'));
    document.getElementById('whitelabel-permission-alert')?.classList.toggle('hidden', hasPermission('ManageWhiteLabel'));
    document.getElementById('clients-permission-alert')?.classList.toggle('hidden', hasPermission('ManageClients'));
    document.getElementById('subs-permission-alert')?.classList.toggle('hidden', hasPermission('ManageSubscriptions'));
    document.getElementById('cash-permission-alert')?.classList.toggle('hidden', hasPermission('GenerateCashKeys'));
    document.getElementById('users-permission-alert')?.classList.toggle('hidden', hasPermission('ManageUsers'));
}

// 2. Tab Navigation
function switchTab(tabName) {
    currentTab = tabName;

    // Update active sidebar nav
    document.querySelectorAll('.sidebar-nav .nav-item').forEach(item => {
        item.classList.remove('active');
        if (item.getAttribute('href') === `#${tabName}`) {
            item.classList.add('active');
        }
    });

    // Update Panes
    document.querySelectorAll('.tab-pane').forEach(pane => pane.classList.add('hidden'));
    const targetPane = document.getElementById(`tab-${tabName}`);
    if (targetPane) targetPane.classList.remove('hidden');

    // Title update
    const titleMap = {
        overview: 'Dashboard Overview & Analytics',
        plans: 'Dynamic Plans & Pricing Manager',
        whitelabel: 'White Label Brand Configurator',
        clients: 'Clients Directory',
        subscriptions: 'Subscriptions & API Keys Ledger',
        cashkey: 'Cash Payment API Key Generator',
        users: 'Staff Users & Permission Scopes',
        'email-logs': 'Email License Dispatch Audit Log'
    };
    document.getElementById('tab-title').innerText = titleMap[tabName] || 'Admin Console';

    updateTabPermissionAlerts();
    refreshCurrentTab();
}

function refreshCurrentTab() {
    loadTokenLedgerStatus();
    if (currentTab === 'overview') loadOverviewData();
    else if (currentTab === 'plans') loadPlansTab();
    else if (currentTab === 'whitelabel') loadWhiteLabelTab();
    else if (currentTab === 'clients') loadClientsTab();
    else if (currentTab === 'subscriptions') loadSubscriptionsTab();
    else if (currentTab === 'cashkey') loadCashKeyTab();
    else if (currentTab === 'users') loadUsersTab();
    else if (currentTab === 'email-logs') loadEmailLogsTab();
}

// 3. Overview Tab
async function loadOverviewData() {
    try {
        const [clientsRes, subsRes, plansRes, usersRes] = await Promise.all([
            fetch('/api/v1/admin/clients?page=1&pageSize=1000', { headers: getAuthHeaders() }),
            fetch('/api/v1/admin/subscriptions?page=1&pageSize=1000', { headers: getAuthHeaders() }),
            fetch('/api/v1/admin/plans', { headers: getAuthHeaders() }),
            fetch('/api/v1/admin/users', { headers: getAuthHeaders() })
        ]);

        const clientsData = clientsRes.ok ? await clientsRes.json() : null;
        const subsData = subsRes.ok ? await subsRes.json() : null;
        const plansData = plansRes.ok ? await plansRes.json() : [];
        const usersData = usersRes.ok ? await usersRes.json() : [];

        const clients = (clientsData && clientsData.items) ? clientsData.items : (Array.isArray(clientsData) ? clientsData : []);
        const totalClientsCount = (clientsData && typeof clientsData.totalItems === 'number') ? clientsData.totalItems : clients.length;
        const subs = (subsData && subsData.items) ? subsData.items : (Array.isArray(subsData) ? subsData : []);
        const plans = Array.isArray(plansData) ? plansData : [];
        const users = Array.isArray(usersData) ? usersData : [];

        const kpiClients = document.getElementById('kpi-clients-count');
        const kpiSubs = document.getElementById('kpi-subs-count');
        const kpiPlans = document.getElementById('kpi-plans-count');
        const kpiUsers = document.getElementById('kpi-users-count');

        if (kpiClients) kpiClients.innerText = totalClientsCount || 0;
        if (kpiSubs) kpiSubs.innerText = subs.filter(s => s && s.status === 'Active').length || 0;
        if (kpiPlans) kpiPlans.innerText = plans.length || 0;
        if (kpiUsers) kpiUsers.innerText = users.length || 0;

        const tbody = document.querySelector('#overview-subs-table tbody');
        if (tbody) {
            if (subs.length === 0) {
                tbody.innerHTML = '<tr><td colspan="6" class="text-center text-muted">No subscriptions recorded yet.</td></tr>';
                return;
            }

            tbody.innerHTML = subs.slice(0, 5).map(s => `
                <tr>
                    <td><strong>${escapeHtml(s.clientName)}</strong></td>
                    <td><code>${escapeHtml(s.taxPayerTIN)}</code></td>
                    <td>${escapeHtml(s.planName)}</td>
                    <td><span class="badge-method">${escapeHtml(s.paymentMethod)}</span></td>
                    <td>${s.validTo ? new Date(s.validTo).toLocaleDateString() : '-'}</td>
                    <td><span class="status-pill ${(s.status || '').toLowerCase()}">${escapeHtml(s.status)}</span></td>
                </tr>
            `).join('');
        }
    } catch (err) {
        console.error('Error loading overview data:', err);
    }
}

// 4. Plans Tab (Dynamic Pricing Manager)
async function loadPlansTab() {
    const container = document.getElementById('admin-plans-container');
    if (!container) return;
    try {
        const res = await fetch('/api/v1/admin/plans', { headers: getAuthHeaders() });
        if (!res.ok) {
            const errData = await res.json().catch(() => ({}));
            throw new Error(errData.message || `Server returned HTTP ${res.status}`);
        }
        const data = await res.json();
        allPlans = Array.isArray(data) ? data : [];

        if (allPlans.length === 0) {
            container.innerHTML = '<div class="text-center text-muted" style="grid-column: 1 / -1; padding: 2rem;">No subscription plans configured yet.</div>';
            await loadHardwareTierPricing();
            return;
        }

        container.innerHTML = allPlans.map(p => `
            <div class="plan-admin-card">
                <div>
                    <div class="flex-between">
                        <h4>${escapeHtml(p.planName)}</h4>
                        <span class="status-pill ${p.isActive ? 'active' : 'expired'}">${p.isActive ? 'Active' : 'Inactive'}</span>
                    </div>
                    <div class="price">$${(p.price || 0).toFixed(2)} <small>/ device / ${escapeHtml(p.billingCycle)} (${escapeHtml(p.currency)})</small></div>
                    <p class="text-muted-sm">${escapeHtml(p.description)}</p>
                </div>
                <div>
                    <div class="plan-meta-details">
                        <span><i class="fa-solid fa-layer-group"></i> Per Device Pricing</span>
                        <span><i class="fa-solid fa-clock"></i> Grace: ${p.gracePeriodDays || 0} days</span>
                        <span><i class="fa-solid fa-calendar"></i> Duration: ${p.durationMonths || 1} mo</span>
                    </div>
                    <div class="card-actions">
                        <button class="btn-sm btn-edit" onclick="editPlan('${p.id}')"><i class="fa-solid fa-pen-to-square"></i> Edit Plan</button>
                        <button class="btn-sm btn-delete" onclick="deletePlan('${p.id}')"><i class="fa-solid fa-trash-can"></i> Delete</button>
                    </div>
                </div>
            </div>
        `).join('');
        await loadHardwareTierPricing();
    } catch (err) {
        container.innerHTML = `<div class="error-msg" style="grid-column: 1 / -1;">Error loading plans: ${escapeHtml(err.message)}</div>`;
    }
}

function openPlanModal(planId = null) {
    if (!hasPermission('ManagePlans')) {
        alert('Permission Denied: You do not have the ManagePlans permission.');
        return;
    }

    document.getElementById('plan-form').reset();
    document.getElementById('plan-edit-id').value = '';
    document.getElementById('plan-modal-title').innerText = planId ? 'Edit Subscription Plan' : 'Add Subscription Plan';

    if (planId) {
        const p = allPlans.find(x => x.id === planId);
        if (p) {
            document.getElementById('plan-edit-id').value = p.id;
            document.getElementById('pm-name').value = p.planName;
            document.getElementById('pm-cycle').value = p.billingCycle;
            document.getElementById('pm-price').value = p.price;
            document.getElementById('pm-duration').value = p.durationMonths;
            document.getElementById('pm-devices').value = p.maxDevices;
            document.getElementById('pm-grace').value = p.gracePeriodDays;
            document.getElementById('pm-currency').value = p.currency;
            document.getElementById('pm-desc').value = p.description;
        }
    }

    document.getElementById('plan-modal').classList.remove('hidden');
}

function closePlanModal() {
    document.getElementById('plan-modal').classList.add('hidden');
}

document.getElementById('plan-form').addEventListener('submit', async (e) => {
    e.preventDefault();
    if (!hasPermission('ManagePlans')) {
        alert('Permission Denied: You do not have the ManagePlans permission.');
        return;
    }

    const id = document.getElementById('plan-edit-id').value;
    const method = id ? 'PUT' : 'POST';
    const url = id ? `/api/v1/admin/plans/${id}` : '/api/v1/admin/plans';

    const payload = {
        planName: document.getElementById('pm-name').value,
        billingCycle: document.getElementById('pm-cycle').value,
        price: parseFloat(document.getElementById('pm-price').value),
        durationMonths: parseInt(document.getElementById('pm-duration').value),
        maxDevices: parseInt(document.getElementById('pm-devices').value),
        gracePeriodDays: parseInt(document.getElementById('pm-grace').value),
        currency: document.getElementById('pm-currency').value,
        description: document.getElementById('pm-desc').value,
        isActive: true
    };

    try {
        const res = await fetch(url, {
            method,
            headers: getAuthHeaders({ 'Content-Type': 'application/json' }),
            body: JSON.stringify(payload)
        });

        if (!res.ok) {
            const data = await res.json().catch(() => ({}));
            throw new Error(data.message || 'Failed to save plan.');
        }
        closePlanModal();
        loadPlansTab();
    } catch (err) {
        alert(err.message);
    }
});

async function deletePlan(id) {
    if (!hasPermission('ManagePlans')) {
        alert('Permission Denied: You do not have the ManagePlans permission.');
        return;
    }

    if (!confirm('Are you sure you want to delete this subscription plan?')) return;
    try {
        const res = await fetch(`/api/v1/admin/plans/${id}`, { method: 'DELETE', headers: getAuthHeaders() });
        if (!res.ok) {
            const data = await res.json().catch(() => ({}));
            throw new Error(data.message || 'Failed to delete plan.');
        }
        loadPlansTab();
    } catch (err) {
        alert(err.message);
    }
}

function editPlan(id) {
    openPlanModal(id);
}

// Hardware Tier Upgrade Pricing Logic
async function loadHardwareTierPricing() {
    try {
        const res = await fetch('/api/v1/admin/subscriptions/tier-pricing', { headers: getAuthHeaders() });
        if (res.ok) {
            hardwareTierPrices = await res.json();
            renderHardwareTierPricingTable();
            updateCashPriceSummary();
        }
    } catch (err) {
        console.error('Failed to load hardware tier pricing:', err);
    }
}

function renderHardwareTierPricingTable() {
    const tbody = document.getElementById('hardware-tier-pricing-tbody');
    if (!tbody) return;

    if (!hardwareTierPrices || hardwareTierPrices.length === 0) {
        tbody.innerHTML = '<tr><td colspan="6" class="text-center text-muted">No hardware tier pricing configured.</td></tr>';
        return;
    }

    tbody.innerHTML = hardwareTierPrices.map(t => `
        <tr>
            <td><strong>${escapeHtml(t.tierName)}</strong></td>
            <td><span class="badge-method">${t.defaultDevices} Device${t.defaultDevices > 1 ? 's' : ''}</span></td>
            <td><span class="badge-method" style="background: rgba(239, 68, 68, 0.15); color: #f87171;">Max Cap ${t.hardCap}</span></td>
            <td>
                <div style="display: flex; align-items: center; gap: 0.25rem;">
                    <span style="font-weight: 700; color: var(--text-color, #94a3b8);">$</span>
                    <input type="number" step="0.01" min="0" class="form-control tier-price-input" data-tier="${escapeHtml(t.tierName)}" value="${t.pricePerSlotUSD.toFixed(2)}" style="width: 110px; font-weight: 700;">
                </div>
            </td>
            <td>
                <input type="number" min="1" class="form-control tier-tokens-input" data-tier="${escapeHtml(t.tierName)}" value="${t.tokensPerSlot}" style="width: 80px; text-align: center;">
            </td>
            <td><input type="text" class="form-control tier-desc-input" data-tier="${escapeHtml(t.tierName)}" value="${escapeHtml(t.description || '')}"></td>
        </tr>
    `).join('');
}

async function saveHardwareTierPricing() {
    if (!hasPermission('ManagePlans')) {
        alert('Permission Denied: You do not have the ManagePlans permission.');
        return;
    }

    const priceInputs = document.querySelectorAll('.tier-price-input');
    const updatedTiers = [];

    priceInputs.forEach(input => {
        const tierName = input.dataset.tier;
        const price = parseFloat(input.value) || 0;
        const tokensInput = document.querySelector(`.tier-tokens-input[data-tier="${tierName}"]`);
        const descInput = document.querySelector(`.tier-desc-input[data-tier="${tierName}"]`);
        const tokens = parseInt(tokensInput?.value) || 1;
        const desc = descInput?.value || '';

        const existing = hardwareTierPrices.find(t => t.tierName.toLowerCase() === tierName.toLowerCase());

        updatedTiers.push({
            id: existing?.id,
            tierName: tierName,
            pricePerSlotUSD: price,
            tokensPerSlot: tokens,
            hardCap: existing?.hardCap || (tierName === 'Cloud' ? 100 : (tierName === 'BranchBox' ? 25 : 5)),
            defaultDevices: existing?.defaultDevices || (tierName === 'Cloud' ? 10 : (tierName === 'BranchBox' ? 5 : 1)),
            description: desc
        });
    });

    try {
        const res = await fetch('/api/v1/admin/subscriptions/tier-pricing', {
            method: 'PUT',
            headers: getAuthHeaders({ 'Content-Type': 'application/json' }),
            body: JSON.stringify({ tiers: updatedTiers })
        });

        if (!res.ok) {
            const errData = await res.json().catch(() => ({}));
            throw new Error(errData.message || 'Failed to update hardware tier prices.');
        }

        hardwareTierPrices = await res.json();
        renderHardwareTierPricingTable();
        updateCashPriceSummary();
        alert('Hardware tier pricing updated successfully!');
    } catch (err) {
        alert('Error updating hardware tier prices: ' + err.message);
    }
}

// 5. White Label Tab Configurator
async function loadWhiteLabelConfig() {
    try {
        const res = await fetch('/api/v1/admin/whitelabel', { headers: getAuthHeaders() });
        if (!res.ok) return;
        const config = await res.json();

        document.getElementById('admin-brand-name').innerText = config.firmName || 'Apex Consultancy';
        if (config.logoUrl) {
            document.getElementById('admin-brand-logo').src = config.logoUrl;
        }

        // Apply primary color to CSS variables in Admin UI
        if (config.primaryColor) {
            document.documentElement.style.setProperty('--primary-color', config.primaryColor);
        }
    } catch (err) {
        console.warn('Error loading white label config:', err);
    }
}

async function loadWhiteLabelTab() {
    try {
        const res = await fetch('/api/v1/admin/whitelabel', { headers: getAuthHeaders() });
        const data = await res.json();

        document.getElementById('wl-firm-name').value = data.firmName || '';
        document.getElementById('wl-tagline').value = data.tagline || '';
        document.getElementById('wl-logo-url').value = data.logoUrl || '';
        document.getElementById('wl-support-email').value = data.supportEmail || '';
        document.getElementById('wl-primary-color').value = data.primaryColor || '#2563eb';
        document.getElementById('wl-primary-hex').value = data.primaryColor || '#2563eb';
        document.getElementById('wl-secondary-color').value = data.secondaryColor || '#1e40af';
        document.getElementById('wl-secondary-hex').value = data.secondaryColor || '#1e40af';
        document.getElementById('wl-support-phone').value = data.supportPhone || '';
        document.getElementById('wl-address').value = data.address || '';
        document.getElementById('wl-copyright').value = data.copyrightText || '';
        document.getElementById('wl-email-footer').value = data.emailFooterText || '';
    } catch (err) {
        console.error('Failed to load white label tab:', err);
    }
}

function setupColorPickerSync() {
    const pPicker = document.getElementById('wl-primary-color');
    const pHex = document.getElementById('wl-primary-hex');
    pPicker?.addEventListener('input', (e) => pHex.value = e.target.value);

    const sPicker = document.getElementById('wl-secondary-color');
    const sHex = document.getElementById('wl-secondary-hex');
    sPicker?.addEventListener('input', (e) => sHex.value = e.target.value);
}

document.getElementById('whitelabel-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();
    if (!hasPermission('ManageWhiteLabel')) {
        alert('Permission Denied: You do not have the ManageWhiteLabel permission.');
        return;
    }

    const payload = {
        firmName: document.getElementById('wl-firm-name').value,
        tagline: document.getElementById('wl-tagline').value,
        logoUrl: document.getElementById('wl-logo-url').value,
        supportEmail: document.getElementById('wl-support-email').value,
        primaryColor: document.getElementById('wl-primary-color').value,
        secondaryColor: document.getElementById('wl-secondary-color').value,
        supportPhone: document.getElementById('wl-support-phone').value,
        address: document.getElementById('wl-address').value,
        copyrightText: document.getElementById('wl-copyright').value,
        emailFooterText: document.getElementById('wl-email-footer').value
    };

    try {
        const res = await fetch('/api/v1/admin/whitelabel', {
            method: 'PUT',
            headers: getAuthHeaders({ 'Content-Type': 'application/json' }),
            body: JSON.stringify(payload)
        });

        if (!res.ok) {
            const errData = await res.json().catch(() => ({}));
            throw new Error(errData.message || 'Failed to update white label settings.');
        }
        alert('White Label settings updated successfully!');
        loadWhiteLabelConfig();
    } catch (err) {
        alert(err.message);
    }
});

// 6. Clients Tab
async function loadClientsTab() {
    const tbody = document.getElementById('clients-table-body');
    tbody.innerHTML = '<tr><td colspan="8" class="text-center">Loading clients...</td></tr>';

    try {
        const queryParams = new URLSearchParams({
            search: clientState.search,
            status: clientState.status,
            page: clientState.page.toString(),
            pageSize: clientState.pageSize.toString()
        });

        const res = await fetch(`/api/v1/admin/clients?${queryParams.toString()}`, { headers: getAuthHeaders() });
        if (!res.ok) throw new Error(`HTTP ${res.status}`);
        
        const data = await res.json();
        
        if (data && data.items) {
            allClients = data.items;
            clientState.page = data.page;
            clientState.pageSize = data.pageSize;
            clientState.totalItems = data.totalItems;
            clientState.totalPages = data.totalPages;
        } else if (Array.isArray(data)) {
            allClients = data;
            clientState.totalItems = data.length;
            clientState.totalPages = 1;
        } else {
            allClients = [];
            clientState.totalItems = 0;
            clientState.totalPages = 1;
        }

        renderClientsTable(allClients);
        updateClientPaginationControls();
    } catch (err) {
        tbody.innerHTML = `<tr><td colspan="8" class="text-center text-danger">Error loading clients: ${err.message}</td></tr>`;
    }
}

function updateClientPaginationControls() {
    const info = document.getElementById('client-pagination-info');
    const indicator = document.getElementById('client-page-indicator');
    const prevBtn = document.getElementById('btn-client-prev');
    const nextBtn = document.getElementById('btn-client-next');

    if (!info || !indicator || !prevBtn || !nextBtn) return;

    if (clientState.totalItems === 0) {
        info.innerText = 'Showing 0 of 0 clients';
        indicator.innerText = 'Page 1 of 1';
        prevBtn.disabled = true;
        nextBtn.disabled = true;
        return;
    }

    const start = (clientState.page - 1) * clientState.pageSize + 1;
    const end = Math.min(clientState.page * clientState.pageSize, clientState.totalItems);
    info.innerText = `Showing ${start} to ${end} of ${clientState.totalItems} clients`;
    indicator.innerText = `Page ${clientState.page} of ${clientState.totalPages}`;

    prevBtn.disabled = clientState.page <= 1;
    nextBtn.disabled = clientState.page >= clientState.totalPages;
}

function onClientSearchInput() {
    clearTimeout(clientDebounceTimer);
    clientDebounceTimer = setTimeout(() => {
        clientState.search = document.getElementById('client-search-input').value.trim();
        clientState.page = 1;
        loadClientsTab();
    }, 300);
}

function onClientFilterChange() {
    clientState.status = document.getElementById('client-status-filter').value;
    clientState.page = 1;
    loadClientsTab();
}

function onClientPageSizeChange() {
    clientState.pageSize = parseInt(document.getElementById('client-pagesize-select').value, 10);
    clientState.page = 1;
    loadClientsTab();
}

function clearClientFilters() {
    document.getElementById('client-search-input').value = '';
    document.getElementById('client-status-filter').value = 'All';
    clientState.search = '';
    clientState.status = 'All';
    clientState.page = 1;
    loadClientsTab();
}

function changeClientPage(delta) {
    const newPage = clientState.page + delta;
    if (newPage >= 1 && newPage <= clientState.totalPages) {
        clientState.page = newPage;
        loadClientsTab();
    }
}

function renderClientsTable(clients) {
    const tbody = document.getElementById('clients-table-body');
    if (clients.length === 0) {
        tbody.innerHTML = '<tr><td colspan="9" class="text-center">No clients registered.</td></tr>';
        return;
    }

    tbody.innerHTML = clients.map(c => {
        const apiKey = c.tenantApiKey || '-';
        return `
        <tr>
            <td><strong>${escapeHtml(c.companyName)}</strong></td>
            <td><code>${escapeHtml(c.taxPayerTIN)}</code></td>
            <td>${escapeHtml(c.contactName || '-')}</td>
            <td>${escapeHtml(c.email)}</td>
            <td>${escapeHtml(c.phoneNumber || '-')}</td>
            <td>
                <div style="display: flex; align-items: center; gap: 0.35rem;">
                    <code style="font-size: 0.78rem; font-weight: 700; color: #818cf8; background: rgba(129, 140, 248, 0.1); padding: 2px 6px; border-radius: 4px;">${escapeHtml(apiKey)}</code>
                    <button class="btn-sm" style="padding: 2px 6px; font-size: 0.75rem;" title="Copy Tenant API Key" onclick="copyClientApiKey('${escapeHtml(apiKey)}')">
                        <i class="fa-solid fa-copy"></i>
                    </button>
                </div>
            </td>
            <td><span class="status-pill active">${c.activeSubscriptionsCount} Active</span></td>
            <td><span class="status-pill ${c.status.toLowerCase() === 'active' ? 'active' : 'expired'}">${c.status}</span></td>
            <td>
                <button class="btn-sm btn-edit" onclick="editClient('${c.id}')"><i class="fa-solid fa-pen-to-square"></i> Edit</button>
                <button class="btn-sm" onclick="quickCashKeyForTin('${c.taxPayerTIN}')"><i class="fa-solid fa-plus"></i> Issue Key</button>
            </td>
        </tr>
    `;
    }).join('');
}

function copyClientApiKey(key) {
    if (!key || key === '-') return;
    if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(key).then(() => {
            alert('Tenant API Key copied to clipboard!\n\nKey: ' + key);
        }).catch(() => fallbackCopyKey(key));
    } else {
        fallbackCopyKey(key);
    }
}

function fallbackCopyKey(key) {
    const temp = document.createElement('textarea');
    temp.value = key;
    document.body.appendChild(temp);
    temp.select();
    document.execCommand('copy');
    document.body.removeChild(temp);
    alert('Tenant API Key copied to clipboard!\n\nKey: ' + key);
}

function copyClientModalApiKey() {
    const key = document.getElementById('cm-apikey').value;
    if (!key) {
        alert('API Key will be generated automatically when you save the client profile.');
        return;
    }
    copyClientApiKey(key);
}

function openClientModal(clientId = null) {
    if (!hasPermission('ManageClients')) {
        alert('Permission Denied: You do not have the ManageClients permission.');
        return;
    }

    document.getElementById('client-form').reset();
    document.getElementById('cm-id').value = '';
    document.getElementById('cm-apikey').value = '';
    document.getElementById('client-modal-title').innerText = clientId ? 'Edit Client Profile' : 'Add New Client';

    if (clientId) {
        const c = allClients.find(x => x.id === clientId);
        if (c) {
            document.getElementById('cm-id').value = c.id;
            document.getElementById('cm-company').value = c.companyName || '';
            document.getElementById('cm-tin').value = c.taxPayerTIN || '';
            document.getElementById('cm-contact').value = c.contactName || '';
            document.getElementById('cm-email').value = c.email || '';
            document.getElementById('cm-phone').value = c.phoneNumber || '';
            document.getElementById('cm-status').value = c.status || 'Active';
            document.getElementById('cm-address').value = c.address || '';
            document.getElementById('cm-apikey').value = c.tenantApiKey || '';
        }
    }

    document.getElementById('client-modal').classList.remove('hidden');
}

function closeClientModal() {
    document.getElementById('client-modal').classList.add('hidden');
}

function editClient(id) {
    openClientModal(id);
}

document.getElementById('client-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();
    if (!hasPermission('ManageClients')) {
        alert('Permission Denied: You do not have the ManageClients permission.');
        return;
    }

    const id = document.getElementById('cm-id').value;
    const method = id ? 'PUT' : 'POST';
    const url = id ? `/api/v1/admin/clients/${id}` : '/api/v1/admin/clients';

    const payload = {
        companyName: document.getElementById('cm-company').value.trim(),
        taxPayerTIN: document.getElementById('cm-tin').value.trim(),
        contactName: document.getElementById('cm-contact').value.trim(),
        email: document.getElementById('cm-email').value.trim(),
        phoneNumber: document.getElementById('cm-phone').value.trim(),
        status: document.getElementById('cm-status').value,
        address: document.getElementById('cm-address').value.trim()
    };

    try {
        const res = await fetch(url, {
            method,
            headers: getAuthHeaders({ 'Content-Type': 'application/json' }),
            body: JSON.stringify(payload)
        });

        const data = await res.json();

        if (!res.ok) {
            throw new Error(data.message || 'Failed to save client profile.');
        }

        if (!id && data.tenantApiKey) {
            alert(`Client created successfully!\n\nAuto-Generated Tenant API Key:\n${data.tenantApiKey}\n\nThis key has been automatically copied to your clipboard.`);
            copyClientApiKey(data.tenantApiKey);
        }

        closeClientModal();
        loadClientsTab();
    } catch (err) {
        alert('Error: ' + err.message);
    }
});

// 7. Subscriptions Ledger Tab
async function loadSubscriptionsTab() {
    const tbody = document.getElementById('subs-table-body');
    const info = document.getElementById('sub-pagination-info');
    const pageIndicator = document.getElementById('sub-page-indicator');
    const btnPrev = document.getElementById('btn-sub-prev');
    const btnNext = document.getElementById('btn-sub-next');

    try {
        const queryParams = new URLSearchParams({
            page: subState.page,
            pageSize: subState.pageSize,
            search: subState.search,
            status: subState.status
        });

        const res = await fetch(`/api/v1/admin/subscriptions?${queryParams.toString()}`, { headers: getAuthHeaders() });
        const data = await res.json();

        const subs = (data && data.items) ? data.items : (Array.isArray(data) ? data : []);
        subState.totalItems = data.totalItems !== undefined ? data.totalItems : subs.length;
        subState.totalPages = data.totalPages || Math.ceil(subState.totalItems / subState.pageSize) || 1;
        subState.page = data.page || subState.page;

        if (!subs || subs.length === 0) {
            tbody.innerHTML = '<tr><td colspan="11" class="text-center">No matching subscription records found.</td></tr>';
            if (info) info.innerText = 'Showing 0 of 0 subscriptions';
            if (pageIndicator) pageIndicator.innerText = 'Page 1 of 1';
            if (btnPrev) btnPrev.disabled = true;
            if (btnNext) btnNext.disabled = true;
            return;
        }

        tbody.innerHTML = subs.map(s => {
            const devCount = (s.maxDevices !== undefined && s.maxDevices !== null) ? s.maxDevices : 1;
            const durationMonths = (s.validFrom && s.validTo) ? Math.max(1, Math.round((new Date(s.validTo) - new Date(s.validFrom)) / (1000 * 3600 * 24 * 30.4375))) : 1;
            const tokenCount = (s.tokensConsumed !== undefined && s.tokensConsumed !== null) ? s.tokensConsumed : (devCount * durationMonths);

            return `
            <tr>
                <td><strong>${escapeHtml(s.clientName)}</strong></td>
                <td><code>${escapeHtml(s.taxPayerTIN)}</code></td>
                <td>${escapeHtml(s.planName)}</td>
                <td>$${s.price.toFixed(2)}</td>
                <td><span class="badge-method" style="background: rgba(99, 102, 241, 0.12); color: #818cf8; border: 1px solid rgba(99, 102, 241, 0.3); font-weight: 600;">${devCount} Dev</span></td>
                <td><strong style="color: #34d399;"><i class="fa-solid fa-coins"></i> ${tokenCount}</strong></td>
                <td>${new Date(s.validFrom).toLocaleDateString()} - ${new Date(s.validTo).toLocaleDateString()}</td>
                <td><strong>${s.daysRemaining} days</strong></td>
                <td><span class="badge-method">${s.paymentMethod}</span> (${escapeHtml(s.transactionRef)})</td>
                <td><span class="status-pill ${s.status.toLowerCase()}">${s.status}</span></td>
                <td>
                    <div style="display: flex; gap: 6px; align-items: center;">
                        <button class="btn-sm" title="Copy Key" onclick="copyKeyText('${escapeHtml(s.fullApiKey)}')">
                            <i class="fa-solid fa-copy"></i>
                        </button>
                        <button class="btn-sm ${s.isActive ? 'btn-delete' : 'btn-edit'}" title="${s.isActive ? 'Deactivate' : 'Activate'}" onclick="toggleSubscriptionActive('${s.id}')">
                            <i class="fa-solid ${s.isActive ? 'fa-ban' : 'fa-check'}"></i>
                        </button>
                    </div>
                </td>
            </tr>
            `;
        }).join('');

        const startItem = (subState.page - 1) * subState.pageSize + 1;
        const endItem = Math.min(subState.page * subState.pageSize, subState.totalItems);
        if (info) info.innerText = `Showing ${startItem} to ${endItem} of ${subState.totalItems} subscriptions`;
        if (pageIndicator) pageIndicator.innerText = `Page ${subState.page} of ${subState.totalPages}`;

        if (btnPrev) btnPrev.disabled = subState.page <= 1;
        if (btnNext) btnNext.disabled = subState.page >= subState.totalPages;

    } catch (err) {
        tbody.innerHTML = `<tr><td colspan="11" class="text-center text-danger">Error loading subscriptions: ${err.message}</td></tr>`;
    }
}

function onSubSearchInput() {
    clearTimeout(subDebounceTimer);
    subDebounceTimer = setTimeout(() => {
        subState.search = document.getElementById('sub-search-input').value.trim();
        subState.page = 1;
        loadSubscriptionsTab();
    }, 300);
}

function onSubFilterChange() {
    subState.status = document.getElementById('sub-status-filter').value;
    subState.page = 1;
    loadSubscriptionsTab();
}

function onSubPageSizeChange() {
    subState.pageSize = parseInt(document.getElementById('sub-pagesize-select').value, 10) || 10;
    subState.page = 1;
    loadSubscriptionsTab();
}

function clearSubFilters() {
    document.getElementById('sub-search-input').value = '';
    document.getElementById('sub-status-filter').value = 'All';
    subState.search = '';
    subState.status = 'All';
    subState.page = 1;
    loadSubscriptionsTab();
}

function changeSubPage(delta) {
    const newPage = subState.page + delta;
    if (newPage >= 1 && newPage <= subState.totalPages) {
        subState.page = newPage;
        loadSubscriptionsTab();
    }
}

function copyKeyText(key) {
    navigator.clipboard.writeText(key);
    alert('API Key copied to clipboard!');
}

async function toggleSubscriptionActive(id) {
    if (!hasPermission('ManageSubscriptions')) {
        alert('Permission Denied: You do not have the ManageSubscriptions permission.');
        return;
    }

    try {
        const res = await fetch(`/api/v1/admin/subscriptions/${id}/toggle-status`, {
            method: 'PUT',
            headers: getAuthHeaders()
        });
        if (!res.ok) {
            const data = await res.json().catch(() => ({}));
            throw new Error(data.message || 'Failed to update subscription status.');
        }
        loadSubscriptionsTab();
        if (currentTab === 'clients') loadClientsTab();
    } catch (err) {
        alert(err.message);
    }
}

// 8. Cash Payment API Key Generator Tab
async function loadCashKeyTab() {
    const select = document.getElementById('cash-plan-select');
    const alertBox = document.getElementById('cash-permission-alert');
    const btn = document.getElementById('btn-generate-cash');

    // Check Permission
    const canGenerate = hasPermission('GenerateCashKeys');
    if (alertBox) alertBox.classList.toggle('hidden', canGenerate);
    if (btn) btn.disabled = !canGenerate;

    if (!select) return;

    try {
        const res = await fetch('/api/v1/public/plans');
        if (!res.ok) {
            select.innerHTML = '<option value="">Error loading subscription plans</option>';
            return;
        }
        const data = await res.json();
        allPlans = Array.isArray(data) ? data : [];
        if (allPlans.length === 0) {
            select.innerHTML = '<option value="">No subscription plans available</option>';
            return;
        }
        select.innerHTML = allPlans.map(p => `
            <option value="${p.id}">${escapeHtml(p.planName)} - $${(p.price || 0).toFixed(2)} / dev / ${escapeHtml(p.billingCycle)}</option>
        `).join('');
        onCashActionChanged();
    } catch (err) {
        select.innerHTML = '<option value="">Error loading plans</option>';
    }
}

function onCashActionChanged() {
    const actionType = document.getElementById('cash-action-type')?.value || 'Subscription';
    const subFields = document.getElementById('cash-subscription-fields');
    const upgFields = document.getElementById('cash-upgrade-fields');
    const btn = document.getElementById('btn-generate-cash');

    if (actionType === 'DeviceUpgrade') {
        if (subFields) subFields.classList.add('hidden');
        if (upgFields) upgFields.classList.remove('hidden');
        if (btn) btn.innerHTML = '<i class="fa-solid fa-microchip"></i> Process Cash & Generate Hardware Upgrade Key';
    } else {
        if (subFields) subFields.classList.remove('hidden');
        if (upgFields) upgFields.classList.add('hidden');
        if (btn) btn.innerHTML = '<i class="fa-solid fa-key"></i> Process Cash & Generate Subscription API Key';
    }

    updateCashPriceSummary();
}

function updateCashPriceSummary() {
    const actionType = document.getElementById('cash-action-type')?.value || 'Subscription';
    const totalEl = document.getElementById('cash-price-total') || document.getElementById('cash-price-summary');
    const breakdownEl = document.getElementById('cash-price-breakdown');
    const subtext = document.getElementById('cash-summary-subtext');
    if (!totalEl) return;

    if (actionType === 'DeviceUpgrade') {
        let slots = parseInt(document.getElementById('cash-slots-to-add')?.value) || 1;
        if (slots < 1) slots = 1;

        const selectedTier = document.getElementById('cash-hw-tier')?.value || 'TillStick';
        const tierConfig = hardwareTierPrices.find(t => t.tierName.toLowerCase() === selectedTier.toLowerCase());

        const unitPrice = tierConfig ? tierConfig.pricePerSlotUSD : (selectedTier === 'Cloud' ? 300.00 : (selectedTier === 'BranchBox' ? 150.00 : 50.00));
        const tokensPerSlot = tierConfig ? tierConfig.tokensPerSlot : (selectedTier === 'Cloud' ? 5 : (selectedTier === 'BranchBox' ? 3 : 1));

        const total = slots * unitPrice;
        const totalTokens = slots * tokensPerSlot;

        totalEl.innerText = `$${total.toFixed(2)} USD`;
        if (breakdownEl) {
            breakdownEl.innerText = `$${unitPrice.toFixed(2)} / slot (${totalTokens} Upgrade Token${totalTokens > 1 ? 's' : ''} [${selectedTier}]) × ${slots} slot${slots > 1 ? 's' : ''}`;
        }
        if (subtext) {
            subtext.innerText = `Dynamic Hardware Tier Rate: ${selectedTier} ($${unitPrice.toFixed(2)} / slot, ${tokensPerSlot} Token${tokensPerSlot > 1 ? 's' : ''}/slot)`;
        }
        return;
    }

    if (subtext) subtext.innerText = 'Base plan price × requested device licenses';

    const planSelect = document.getElementById('cash-plan-select');
    if (!planSelect) return;
    
    const planId = planSelect.value;
    const plan = allPlans.find(p => p.id === planId);

    let deviceCount = parseInt(document.getElementById('cash-device-count')?.value) || 1;
    if (deviceCount < 1) deviceCount = 1;

    if (!plan) {
        totalEl.innerText = '$0.00 USD';
        if (breakdownEl) breakdownEl.innerText = '$0.00 × 0 devices';
        return;
    }

    const total = plan.price * deviceCount;
    const totalText = `$${total.toFixed(2)} USD`;
    const breakdownText = `$${plan.price.toFixed(2)} × ${deviceCount} device${deviceCount > 1 ? 's' : ''}`;

    if (breakdownEl) {
        totalEl.innerText = totalText;
        breakdownEl.innerText = breakdownText;
    } else {
        totalEl.innerText = `${totalText} (${breakdownText})`;
    }
}

async function populateCashKeyCompanyDetails(tin) {
    if (!tin) return;

    const tinEl = document.getElementById('cash-tin');
    if (tinEl && tinEl.value !== tin) tinEl.value = tin;

    let client = allClients.find(c => c.taxPayerTIN === tin);

    if (!client) {
        try {
            const res = await fetch('/api/v1/public/validate-tin', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ taxPayerTIN: tin })
            });
            if (res.ok) {
                const data = await res.json();
                if (data.isRegistered) {
                    client = data;
                }
            }
        } catch (err) {
            console.warn('Failed to fetch client details for TIN:', err);
        }
    }

    if (client) {
        const companyEl = document.getElementById('cash-company');
        const emailEl = document.getElementById('cash-email');
        const contactEl = document.getElementById('cash-contact');

        if (companyEl) companyEl.value = client.companyName || '';
        if (emailEl) emailEl.value = client.email || '';
        if (contactEl) contactEl.value = client.contactName || '';
    }
}

async function quickCashKeyForTin(tin) {
    if (!hasPermission('GenerateCashKeys')) {
        alert('Permission Denied: You do not have the GenerateCashKeys permission.');
        return;
    }

    switchTab('cashkey');
    const actionSelect = document.getElementById('cash-action-type');
    if (actionSelect) {
        actionSelect.value = 'Subscription';
        onCashActionChanged();
    }
    await populateCashKeyCompanyDetails(tin);
    if (document.getElementById('cash-device-count')) {
        document.getElementById('cash-device-count').value = 1;
    }
    updateCashPriceSummary();
}

let cashTinDebounceTimer = null;
function onCashTinInput() {
    clearTimeout(cashTinDebounceTimer);
    cashTinDebounceTimer = setTimeout(() => {
        const tin = document.getElementById('cash-tin').value.trim();
        if (tin.length >= 5) {
            populateCashKeyCompanyDetails(tin);
        }
    }, 300);
}

document.getElementById('cash-key-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();
    if (!hasPermission('GenerateCashKeys')) {
        alert('Permission Denied: You do not have the GenerateCashKeys permission.');
        return;
    }

    const actionType = document.getElementById('cash-action-type')?.value || 'Subscription';
    const btn = document.getElementById('btn-generate-cash');
    btn.disabled = true;
    btn.innerHTML = '<i class="fa-solid fa-circle-notch fa-spin"></i> Processing Request...';

    if (actionType === 'DeviceUpgrade') {
        const payload = {
            taxPayerTIN: document.getElementById('cash-tin').value.trim(),
            driveInstanceId: document.getElementById('cash-drive-id').value.trim().toUpperCase(),
            hardwareTier: document.getElementById('cash-hw-tier').value,
            slotsToAdd: parseInt(document.getElementById('cash-slots-to-add').value) || 1,
            currentMaxDevices: document.getElementById('cash-hw-tier').value === 'Cloud' ? 10 : (document.getElementById('cash-hw-tier').value === 'BranchBox' ? 5 : 1),
            cashReceiptRef: document.getElementById('cash-ref').value.trim()
        };

        if (!payload.taxPayerTIN || !payload.driveInstanceId || !payload.cashReceiptRef) {
            alert('Taxpayer TIN, Drive Instance ID, and Receipt Reference are required.');
            btn.disabled = false;
            btn.innerHTML = '<i class="fa-solid fa-microchip"></i> Process Cash & Generate Hardware Upgrade Key';
            return;
        }

        try {
            const res = await fetch('/api/v1/admin/subscriptions/generate-upgrade-key', {
                method: 'POST',
                headers: getAuthHeaders({ 'Content-Type': 'application/json' }),
                body: JSON.stringify(payload)
            });

            const data = await res.json();
            btn.disabled = false;
            btn.innerHTML = '<i class="fa-solid fa-microchip"></i> Process Cash & Generate Hardware Upgrade Key';

            if (!res.ok) {
                alert(data.message || 'Failed to generate Device Upgrade Key.');
                return;
            }

            const titleEl = document.getElementById('cash-result-title');
            if (titleEl) titleEl.innerHTML = '<i class="fa-solid fa-circle-check"></i> Device Upgrade Key Issued Successfully!';

            document.getElementById('generated-key-output').value = data.upgradeKey;
            
            const emailClient = document.getElementById('cash-email')?.value.trim();
            const sendEmail = document.getElementById('cash-send-email')?.checked;
            document.getElementById('cash-email-note').innerText = (sendEmail && emailClient) ?
                `Device upgrade key issued for ${data.hardwareTier} (${data.slotsAdded} slot(s)). Email notice dispatched to ${emailClient}.` :
                `Device upgrade key issued for ${data.hardwareTier} (${data.slotsAdded} slot(s)).`;
            
            document.getElementById('cash-result-card').classList.remove('hidden');
            await loadTokenLedgerStatus();
        } catch (err) {
            btn.disabled = false;
            btn.innerHTML = '<i class="fa-solid fa-microchip"></i> Process Cash & Generate Hardware Upgrade Key';
            alert('Error generating upgrade key: ' + err.message);
        }
        return;
    }

    const payload = {
        taxPayerTIN: document.getElementById('cash-tin').value.trim(),
        companyName: document.getElementById('cash-company').value.trim(),
        clientEmail: document.getElementById('cash-email').value.trim(),
        contactName: document.getElementById('cash-contact').value.trim(),
        planId: document.getElementById('cash-plan-select').value,
        deviceCount: parseInt(document.getElementById('cash-device-count').value) || 1,
        driveInstanceId: document.getElementById('cash-drive-id').value.trim().toUpperCase(),
        hardwareTier: document.getElementById('cash-hw-tier').value,
        cashReceiptRef: document.getElementById('cash-ref').value.trim(),
        sendEmailReceipt: document.getElementById('cash-send-email').checked
    };

    if (!payload.taxPayerTIN || !payload.companyName || !payload.driveInstanceId || !payload.cashReceiptRef) {
        alert('Taxpayer TIN, Company Name, Drive Instance ID, and Receipt Reference are required.');
        btn.disabled = false;
        btn.innerHTML = '<i class="fa-solid fa-key"></i> Process Cash & Generate Subscription API Key';
        return;
    }

    try {
        const res = await fetch('/api/v1/admin/subscriptions/cash-payment', {
            method: 'POST',
            headers: getAuthHeaders({ 'Content-Type': 'application/json' }),
            body: JSON.stringify(payload)
        });

        const data = await res.json();
        btn.disabled = false;
        btn.innerHTML = '<i class="fa-solid fa-key"></i> Process Cash & Generate Subscription API Key';

        if (!res.ok) {
            alert(data.message || 'Failed to generate cash API Key.');
            return;
        }

        const titleEl = document.getElementById('cash-result-title');
        if (titleEl) titleEl.innerHTML = '<i class="fa-solid fa-circle-check"></i> Cash Subscription API Key Issued Successfully!';

        document.getElementById('generated-key-output').value = data.fullApiKey;
        document.getElementById('cash-email-note').innerText = payload.sendEmailReceipt ? 
            `Receipt email with full key dispatched to ${data.clientEmail}` : 'Email receipt skipped.';
        document.getElementById('cash-result-card').classList.remove('hidden');
        await loadTokenLedgerStatus();
    } catch (err) {
        btn.disabled = false;
        btn.innerHTML = '<i class="fa-solid fa-key"></i> Process Cash & Generate Subscription API Key';
        alert('Error: ' + err.message);
    }
});

function copyGeneratedCashKey() {
    const val = document.getElementById('generated-key-output').value;
    navigator.clipboard.writeText(val);
    alert('API Key copied to clipboard!');
}

// 9. Users & Permissions Tab
async function loadUsersTab() {
    const tbody = document.getElementById('users-table-body');
    if (!tbody) return;
    try {
        const res = await fetch('/api/v1/admin/users', { headers: getAuthHeaders() });
        if (!res.ok) {
            const errData = await res.json().catch(() => ({}));
            throw new Error(errData.message || `Server returned HTTP ${res.status}`);
        }
        const data = await res.json();
        allUsers = Array.isArray(data) ? data : [];

        if (allUsers.length === 0) {
            tbody.innerHTML = '<tr><td colspan="7" class="text-center text-muted">No admin users found.</td></tr>';
            return;
        }

        tbody.innerHTML = allUsers.map(u => {
            const perms = Array.isArray(u.permissions) ? u.permissions : [];
            return `
            <tr>
                <td><strong>${escapeHtml(u.username)}</strong></td>
                <td>${escapeHtml(u.fullName)}</td>
                <td>${escapeHtml(u.email)}</td>
                <td><span class="badge-role">${escapeHtml(u.role)}</span></td>
                <td>
                    ${perms.map(p => `<span class="status-pill active">${escapeHtml(p)}</span>`).join(' ')}
                </td>
                <td><span class="status-pill ${u.isActive ? 'active' : 'expired'}">${u.isActive ? 'Active' : 'Disabled'}</span></td>
                <td>
                    <div style="display: flex; gap: 6px; align-items: center;">
                        <button class="btn-sm btn-edit" onclick="openUserModal('${u.id}')">
                            <i class="fa-solid fa-pen-to-square"></i> Edit
                        </button>
                        <button class="btn-sm ${u.isActive ? 'btn-delete' : ''}" onclick="toggleUserStatus('${u.id}', ${!u.isActive})">
                            <i class="fa-solid ${u.isActive ? 'fa-ban' : 'fa-check'}"></i> ${u.isActive ? 'Disable' : 'Enable'}
                        </button>
                    </div>
                </td>
            </tr>
            `;
        }).join('');
    } catch (err) {
        tbody.innerHTML = `<tr><td colspan="7" class="text-center text-danger">Error loading users: ${escapeHtml(err.message)}</td></tr>`;
    }
}

function openUserModal(userId = null) {
    if (!hasPermission('ManageUsers')) {
        alert('Permission Denied: You do not have the ManageUsers permission.');
        return;
    }

    document.getElementById('user-form').reset();
    document.querySelectorAll('.perm-check').forEach(cb => cb.checked = false);

    const titleEl = document.getElementById('user-modal-title');
    const submitBtn = document.getElementById('btn-save-user');
    const usernameInput = document.getElementById('um-username');
    const passwordInput = document.getElementById('um-password');
    const passwordLabel = document.getElementById('um-password-label');
    const idInput = document.getElementById('um-id');

    if (userId) {
        const user = allUsers.find(u => u.id === userId);
        if (user) {
            if (idInput) idInput.value = user.id;
            if (titleEl) titleEl.innerText = 'Edit Staff User Account';
            if (submitBtn) submitBtn.innerHTML = '<i class="fa-solid fa-floppy-disk"></i> Update Account';

            usernameInput.value = user.username || '';
            usernameInput.disabled = true;

            document.getElementById('um-fullname').value = user.fullName || '';
            document.getElementById('um-email').value = user.email || '';

            if (passwordLabel) passwordLabel.innerText = 'Password (Optional)';
            passwordInput.required = false;
            passwordInput.value = '';
            passwordInput.placeholder = '•••••••• (leave blank to keep current)';

            document.getElementById('um-role').value = user.role || 'Staff';

            if (user.permissions && Array.isArray(user.permissions)) {
                document.querySelectorAll('.perm-check').forEach(cb => {
                    if (user.permissions.includes(cb.value)) {
                        cb.checked = true;
                    }
                });
            }
        }
    } else {
        if (idInput) idInput.value = '';
        if (titleEl) titleEl.innerText = 'Create Staff User Account';
        if (submitBtn) submitBtn.innerHTML = '<i class="fa-solid fa-user-plus"></i> Create Account';

        usernameInput.value = '';
        usernameInput.disabled = false;

        if (passwordLabel) passwordLabel.innerText = 'Password *';
        passwordInput.required = true;
        passwordInput.value = '';
        passwordInput.placeholder = '••••••••';
    }

    document.getElementById('user-modal').classList.remove('hidden');
}

function closeUserModal() {
    document.getElementById('user-modal').classList.add('hidden');
}

document.getElementById('user-form')?.addEventListener('submit', async (e) => {
    e.preventDefault();
    if (!hasPermission('ManageUsers')) {
        alert('Permission Denied: You do not have the ManageUsers permission.');
        return;
    }

    const userId = document.getElementById('um-id').value;
    const perms = Array.from(document.querySelectorAll('.perm-check:checked')).map(c => c.value);

    if (userId) {
        const currentUser = allUsers.find(u => u.id === userId);
        const payload = {
            fullName: document.getElementById('um-fullname').value.trim(),
            email: document.getElementById('um-email').value.trim(),
            role: document.getElementById('um-role').value,
            permissions: perms,
            isActive: currentUser ? currentUser.isActive : true
        };
        const password = document.getElementById('um-password').value;
        if (password && password.trim().length > 0) {
            payload.password = password.trim();
        }

        try {
            const res = await fetch(`/api/v1/admin/users/${userId}`, {
                method: 'PUT',
                headers: getAuthHeaders({ 'Content-Type': 'application/json' }),
                body: JSON.stringify(payload)
            });

            if (!res.ok) {
                const errData = await res.json().catch(() => ({}));
                throw new Error(errData.message || 'Failed to update user account.');
            }
            closeUserModal();
            loadUsersTab();
        } catch (err) {
            alert(err.message);
        }
    } else {
        const payload = {
            username: document.getElementById('um-username').value.trim(),
            fullName: document.getElementById('um-fullname').value.trim(),
            email: document.getElementById('um-email').value.trim(),
            password: document.getElementById('um-password').value,
            role: document.getElementById('um-role').value,
            permissions: perms
        };

        try {
            const res = await fetch('/api/v1/admin/users', {
                method: 'POST',
                headers: getAuthHeaders({ 'Content-Type': 'application/json' }),
                body: JSON.stringify(payload)
            });

            if (!res.ok) {
                const errData = await res.json().catch(() => ({}));
                throw new Error(errData.message || 'Failed to create user account.');
            }
            closeUserModal();
            loadUsersTab();
        } catch (err) {
            alert(err.message);
        }
    }
});

async function toggleUserStatus(id, newStatus) {
    if (!hasPermission('ManageUsers')) {
        alert('Permission Denied: You do not have the ManageUsers permission.');
        return;
    }

    try {
        const user = allUsers.find(u => u.id === id);
        if (!user) return;

        const res = await fetch(`/api/v1/admin/users/${id}/permissions`, {
            method: 'PUT',
            headers: getAuthHeaders({ 'Content-Type': 'application/json' }),
            body: JSON.stringify({
                permissions: user.permissions,
                role: user.role,
                isActive: newStatus
            })
        });

        if (!res.ok) {
            const errData = await res.json().catch(() => ({}));
            throw new Error(errData.message || 'Failed to update user status.');
        }

        loadUsersTab();
    } catch (err) {
        alert('Failed to update user status: ' + err.message);
    }
}

// 10. Email Logs Audit Tab & Inspector
let allEmailLogs = [];
let emailState = {
    page: 1,
    pageSize: 10,
    search: '',
    status: 'All',
    totalItems: 0,
    totalPages: 1
};
let selectedEmailLog = null;

async function loadEmailLogsTab() {
    const tbody = document.getElementById('email-logs-table-body');
    try {
        const res = await fetch('/api/v1/admin/email-logs', { headers: getAuthHeaders() });
        if (!res.ok) {
            throw new Error(`Server returned HTTP ${res.status}`);
        }
        allEmailLogs = await res.json();
        updateEmailLogKPIs(allEmailLogs);
        renderEmailLogsTable();
    } catch (err) {
        if (tbody) {
            tbody.innerHTML = `<tr><td colspan="7" class="text-center text-danger">Error loading email logs: ${escapeHtml(err.message)}</td></tr>`;
        }
    }
}

function updateEmailLogKPIs(logs) {
    const total = logs.length;
    const sentCount = logs.filter(l => (l.status || '').toLowerCase() === 'sent' || (l.status || '').toLowerCase() === 'delivered').length;
    const failedCount = logs.filter(l => (l.status || '').toLowerCase() === 'failed' || (l.status || '').toLowerCase() === 'error').length;
    
    let latestTime = '--';
    if (logs.length > 0 && logs[0].sentAt) {
        latestTime = new Date(logs[0].sentAt).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }

    const totalEl = document.getElementById('email-kpi-total');
    const sentEl = document.getElementById('email-kpi-sent');
    const failedEl = document.getElementById('email-kpi-failed');
    const latestEl = document.getElementById('email-kpi-latest');

    if (totalEl) totalEl.innerText = total;
    if (sentEl) sentEl.innerText = sentCount;
    if (failedEl) failedEl.innerText = failedCount;
    if (latestEl) latestEl.innerText = latestTime;
}

function getFilteredEmailLogs() {
    let result = [...allEmailLogs];
    if (emailState.search.trim()) {
        const query = emailState.search.trim().toLowerCase();
        result = result.filter(l =>
            (l.id || '').toLowerCase().includes(query) ||
            (l.recipientEmail || '').toLowerCase().includes(query) ||
            (l.subject || '').toLowerCase().includes(query) ||
            (l.apiKeySent || '').toLowerCase().includes(query)
        );
    }

    if (emailState.status !== 'All') {
        result = result.filter(l => {
            const st = (l.status || '').toLowerCase();
            if (emailState.status === 'Sent') return st === 'sent' || st === 'delivered';
            if (emailState.status === 'Failed') return st === 'failed' || st === 'error';
            return true;
        });
    }

    return result;
}

function renderEmailLogsTable() {
    const tbody = document.getElementById('email-logs-table-body');
    if (!tbody) return;

    const filtered = getFilteredEmailLogs();
    emailState.totalItems = filtered.length;
    emailState.totalPages = Math.max(1, Math.ceil(emailState.totalItems / emailState.pageSize));
    if (emailState.page > emailState.totalPages) emailState.page = emailState.totalPages;

    const startIndex = (emailState.page - 1) * emailState.pageSize;
    const pagedLogs = filtered.slice(startIndex, startIndex + emailState.pageSize);

    if (pagedLogs.length === 0) {
        tbody.innerHTML = '<tr><td colspan="7" class="text-center" style="padding: 2rem; color: #64748b;"><i class="fa-solid fa-inbox" style="font-size: 1.5rem; margin-bottom: 0.5rem; display: block;"></i>No matching email audit logs found.</td></tr>';
    } else {
        tbody.innerHTML = pagedLogs.map(l => {
            const shortId = l.id ? `LOG-${l.id.substring(0, 8)}` : 'LOG-N/A';
            const keyPreview = l.apiKeySent ? `${l.apiKeySent.substring(0, 16)}...` : 'N/A';
            const isSent = (l.status || '').toLowerCase() === 'sent' || (l.status || '').toLowerCase() === 'delivered';
            const statusBadge = isSent
                ? `<span class="status-pill active"><i class="fa-solid fa-circle-check"></i> Sent</span>`
                : `<span class="status-pill expired"><i class="fa-solid fa-circle-xmark"></i> ${escapeHtml(l.status || 'Failed')}</span>`;

            const sentTimeStr = l.sentAt ? new Date(l.sentAt).toLocaleString([], { year: 'numeric', month: 'short', day: '2-digit', hour: '2-digit', minute: '2-digit' }) : 'N/A';

            return `
                <tr>
                    <td><span style="font-family: monospace; font-size: 0.8rem; background: #f1f5f9; padding: 2px 6px; border-radius: 4px; font-weight: 600;">${escapeHtml(shortId)}</span></td>
                    <td><strong><i class="fa-solid fa-envelope" style="color: var(--primary-color); margin-right: 5px;"></i>${escapeHtml(l.recipientEmail)}</strong></td>
                    <td style="font-size: 0.85rem; color: #334155;">${escapeHtml(l.subject)}</td>
                    <td>
                        <div style="display: flex; align-items: center; gap: 6px;">
                            <code style="font-size: 0.8rem; background: #0f172a; color: #38bdf8; padding: 3px 8px; border-radius: 4px;">${escapeHtml(keyPreview)}</code>
                            ${l.apiKeySent ? `<button type="button" class="btn-sm btn-edit" style="padding: 2px 6px; font-size: 0.7rem;" title="Copy Full Key" onclick="copyString('${escapeHtml(l.apiKeySent)}')"><i class="fa-solid fa-copy"></i></button>` : ''}
                        </div>
                    </td>
                    <td>${statusBadge}</td>
                    <td style="font-size: 0.825rem; color: #64748b;"><i class="fa-solid fa-clock" style="margin-right: 4px; font-size: 0.75rem;"></i>${sentTimeStr}</td>
                    <td style="text-align: center;">
                        <button type="button" class="btn-sm btn-edit" onclick="inspectEmailLog('${l.id}')">
                            <i class="fa-solid fa-eye"></i> Inspect
                        </button>
                    </td>
                </tr>
            `;
        }).join('');
    }

    // Update Pagination Bar
    const infoEl = document.getElementById('email-pagination-info');
    const indicatorEl = document.getElementById('email-page-indicator');
    const prevBtn = document.getElementById('btn-email-prev');
    const nextBtn = document.getElementById('btn-email-next');

    const endItem = Math.min(startIndex + emailState.pageSize, emailState.totalItems);
    const startItem = emailState.totalItems > 0 ? startIndex + 1 : 0;

    if (infoEl) infoEl.innerText = `Showing ${startItem} to ${endItem} of ${emailState.totalItems} audit logs`;
    if (indicatorEl) indicatorEl.innerText = `Page ${emailState.page} of ${emailState.totalPages}`;
    if (prevBtn) prevBtn.disabled = (emailState.page <= 1);
    if (nextBtn) nextBtn.disabled = (emailState.page >= emailState.totalPages);
}

function onEmailSearchInput() {
    const input = document.getElementById('email-search-input');
    emailState.search = input ? input.value : '';
    emailState.page = 1;
    renderEmailLogsTable();
}

function onEmailFilterChange() {
    const select = document.getElementById('email-status-filter');
    emailState.status = select ? select.value : 'All';
    emailState.page = 1;
    renderEmailLogsTable();
}

function onEmailPageSizeChange() {
    const select = document.getElementById('email-pagesize-select');
    emailState.pageSize = select ? parseInt(select.value, 10) : 10;
    emailState.page = 1;
    renderEmailLogsTable();
}

function clearEmailFilters() {
    emailState.search = '';
    emailState.status = 'All';
    emailState.page = 1;
    const searchInput = document.getElementById('email-search-input');
    const statusSelect = document.getElementById('email-status-filter');
    if (searchInput) searchInput.value = '';
    if (statusSelect) statusSelect.value = 'All';
    renderEmailLogsTable();
}

function changeEmailPage(delta) {
    const newPage = emailState.page + delta;
    if (newPage >= 1 && newPage <= emailState.totalPages) {
        emailState.page = newPage;
        renderEmailLogsTable();
    }
}

function inspectEmailLog(id) {
    selectedEmailLog = allEmailLogs.find(l => l.id === id);
    if (!selectedEmailLog) return;

    document.getElementById('elm-id').innerText = selectedEmailLog.id || 'N/A';
    document.getElementById('elm-recipient').innerText = selectedEmailLog.recipientEmail || 'N/A';
    document.getElementById('elm-subject').innerText = selectedEmailLog.subject || 'N/A';
    document.getElementById('elm-apikey').innerText = selectedEmailLog.apiKeySent || 'N/A';

    const isSent = (selectedEmailLog.status || '').toLowerCase() === 'sent' || (selectedEmailLog.status || '').toLowerCase() === 'delivered';
    document.getElementById('elm-status').innerHTML = isSent
        ? `<span class="status-pill active"><i class="fa-solid fa-circle-check"></i> Sent</span>`
        : `<span class="status-pill expired"><i class="fa-solid fa-circle-xmark"></i> ${escapeHtml(selectedEmailLog.status || 'Failed')}</span>`;

    const sentAt = selectedEmailLog.sentAt ? new Date(selectedEmailLog.sentAt) : null;
    document.getElementById('elm-sent-at').innerText = sentAt
        ? `${sentAt.toLocaleDateString()} ${sentAt.toLocaleTimeString()} (${sentAt.toUTCString()})`
        : 'N/A';

    // Populate rendered HTML and raw HTML views
    const renderedView = document.getElementById('elm-rendered-view');
    const rawTextarea = document.getElementById('elm-raw-textarea');

    if (renderedView) {
        renderedView.innerHTML = selectedEmailLog.bodyHtml || '<p style="color: #64748b; font-style: italic;">No HTML payload body recorded.</p>';
    }
    if (rawTextarea) {
        rawTextarea.value = selectedEmailLog.bodyHtml || '';
    }

    switchEmailInspectorTab('rendered');
    document.getElementById('email-log-modal').classList.remove('hidden');
}

function closeEmailLogModal() {
    document.getElementById('email-log-modal').classList.add('hidden');
    selectedEmailLog = null;
}

function switchEmailInspectorTab(tab) {
    const renderedView = document.getElementById('elm-rendered-view');
    const rawView = document.getElementById('elm-raw-view');
    const btnRendered = document.getElementById('btn-view-rendered');
    const btnRaw = document.getElementById('btn-view-raw');

    if (tab === 'rendered') {
        renderedView.classList.remove('hidden');
        rawView.classList.add('hidden');
        btnRendered.classList.add('btn-edit');
        btnRendered.style.background = '#eff6ff';
        btnRendered.style.color = '#2563eb';
        btnRaw.classList.remove('btn-edit');
        btnRaw.style.background = '#f1f5f9';
        btnRaw.style.color = '#475569';
    } else {
        renderedView.classList.add('hidden');
        rawView.classList.remove('hidden');
        btnRaw.classList.add('btn-edit');
        btnRaw.style.background = '#eff6ff';
        btnRaw.style.color = '#2563eb';
        btnRendered.classList.remove('btn-edit');
        btnRendered.style.background = '#f1f5f9';
        btnRendered.style.color = '#475569';
    }
}

function copyEmailLogApiKey() {
    if (!selectedEmailLog || !selectedEmailLog.apiKeySent) return;
    copyString(selectedEmailLog.apiKeySent);
}

function copyString(str) {
    if (navigator.clipboard && window.isSecureContext) {
        navigator.clipboard.writeText(str);
        alert('Copied to clipboard!');
    } else {
        const textarea = document.createElement('textarea');
        textarea.value = str;
        document.body.appendChild(textarea);
        textarea.select();
        document.execCommand('copy');
        document.body.removeChild(textarea);
        alert('Copied to clipboard!');
    }
}

function escapeHtml(str) {
    return (str || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}

// 11. Token Ledger & PayNow Top-Up Logic
async function loadTokenLedgerStatus() {
    try {
        const res = await fetch('/api/v1/admin/tokens/status', { headers: getAuthHeaders() });
        if (!res.ok) return;

        const data = await res.json();
        
        const subBalance = data.currentSubscriptionTokenBalance !== undefined ? data.currentSubscriptionTokenBalance : (data.currentTokenBalance || 0);
        const upgBalance = data.currentUpgradeTokenBalance !== undefined ? data.currentUpgradeTokenBalance : 0;
        const totalPurchased = (data.totalSubscriptionTokensPurchased || 0) + (data.totalUpgradeTokensPurchased || 0);

        // Update header badge
        const badgeSpan = document.getElementById('header-token-balance');
        if (badgeSpan) badgeSpan.innerText = subBalance;

        // Update ledger tab KPI displays
        const balDisp = document.getElementById('ledger-balance-display');
        if (balDisp) balDisp.innerText = subBalance;

        const upgDisp = document.getElementById('ledger-upg-balance-display');
        if (upgDisp) upgDisp.innerText = upgBalance;

        const purDisp = document.getElementById('ledger-purchased-display');
        if (purDisp) purDisp.innerText = totalPurchased || data.totalTokensPurchased || 0;

        const immDisp = document.getElementById('ledger-immutability-display');
        if (immDisp) {
            if (data.isTamperDetected) {
                immDisp.innerHTML = `<span style="color: #ef4444;">⚠️ TAMPER ERROR: ${escapeHtml(data.lastTamperError || 'Chain Broken')}</span>`;
            } else {
                immDisp.innerHTML = `<span style="color: #34d399;">✓ Active</span>`;
            }
        }
    } catch (err) {
        console.error('Error loading token status:', err);
    }
}

function loadTokensTab() {
    loadTokenLedgerStatus();
}

function openRedeemVoucherModal() {
    if (!hasPermission('ManageSubscriptions')) {
        alert('Permission Denied: You do not have the ManageSubscriptions permission.');
        return;
    }
    document.getElementById('voucher-code-input').value = '';
    document.getElementById('redeem-voucher-modal').classList.remove('hidden');
}

function openUpgradeKeyModal() {
    if (!hasPermission('ManageSubscriptions')) {
        alert('Permission Denied: You do not have the ManageSubscriptions permission.');
        return;
    }
    document.getElementById('upg-tin').value = '';
    document.getElementById('upg-drive-id').value = '';
    document.getElementById('upg-tier').value = 'TillStick';
    document.getElementById('upg-slots').value = 1;
    document.getElementById('upg-current-max').value = 1;
    document.getElementById('upg-cash-ref').value = '';
    document.getElementById('upg-result-box').classList.add('hidden');
    updateUpgradeSummary();
    document.getElementById('upgrade-key-modal').classList.remove('hidden');
}

function updateUpgradeSummary() {
    const slots = parseInt(document.getElementById('upg-slots')?.value) || 1;
    const price = slots * 50.00;
    const priceEl = document.getElementById('upg-price-display');
    const tokensEl = document.getElementById('upg-tokens-display');

    if (priceEl) priceEl.innerText = `$${price.toFixed(2)} USD`;
    if (tokensEl) tokensEl.innerText = `${slots} Device Upgrade Token${slots > 1 ? 's' : ''} ($50/ea)`;
}

async function handleGenerateUpgradeKey(e) {
    e.preventDefault();
    if (!hasPermission('ManageSubscriptions')) {
        alert('Permission Denied: You do not have the ManageSubscriptions permission.');
        return;
    }

    const btn = document.getElementById('btn-submit-upgrade');
    btn.disabled = true;
    btn.innerHTML = '<i class="fa-solid fa-circle-notch fa-spin"></i> Issuing Upgrade Key...';

    const payload = {
        taxPayerTIN: document.getElementById('upg-tin').value.trim(),
        driveInstanceId: document.getElementById('upg-drive-id').value.trim(),
        hardwareTier: document.getElementById('upg-tier').value,
        slotsToAdd: parseInt(document.getElementById('upg-slots').value) || 1,
        currentMaxDevices: parseInt(document.getElementById('upg-current-max').value) || 1,
        cashReceiptRef: document.getElementById('upg-cash-ref').value.trim()
    };

    try {
        const res = await fetch('/api/v1/admin/subscriptions/generate-upgrade-key', {
            method: 'POST',
            headers: getAuthHeaders({ 'Content-Type': 'application/json' }),
            body: JSON.stringify(payload)
        });

        const data = await res.json();
        btn.disabled = false;
        btn.innerHTML = 'Issue Upgrade Key';

        if (!res.ok) {
            alert(data.message || 'Failed to generate Device Upgrade Key.');
            return;
        }

        document.getElementById('generated-upgrade-key-output').value = data.upgradeKey;
        document.getElementById('upg-result-box').classList.remove('hidden');
        await loadTokenLedgerStatus();
    } catch (err) {
        btn.disabled = false;
        btn.innerHTML = 'Issue Upgrade Key';
        alert('Error: ' + err.message);
    }
}

function copyGeneratedUpgradeKey() {
    const val = document.getElementById('generated-upgrade-key-output').value;
    copyString(val);
}

let pricePerSubscriptionTokenUSD = 5.00;
let pricePerDeviceUpgradeTokenUSD = 50.00;

async function loadTokenPricing() {
    try {
        const res = await fetch('/api/v1/admin/tokens/pricing', {
            headers: getAuthHeaders()
        });
        if (res.ok) {
            const data = await res.json();
            pricePerSubscriptionTokenUSD = data.pricePerTokenUSD || 5.00;
            pricePerDeviceUpgradeTokenUSD = data.pricePerDeviceUpgradeTokenUSD || 50.00;
        }
    } catch (err) {
        console.error('Failed to load token pricing:', err);
    }
}

async function openBuyTokensModal() {
    if (!hasPermission('ManageSubscriptions')) {
        alert('Permission Denied: You do not have the ManageSubscriptions permission.');
        return;
    }
    await loadTokenPricing();
    document.getElementById('buy-token-count').value = 100;
    updateBuyTokenTotal();
    document.getElementById('buy-tokens-modal').classList.remove('hidden');
}

function closeModal(id) {
    document.getElementById(id).classList.add('hidden');
}

function updateBuyTokenTotal() {
    const type = document.getElementById('buy-token-type')?.value || 'Subscription';
    const count = parseInt(document.getElementById('buy-token-count').value) || 0;
    const unitPrice = type === 'DeviceUpgrade' ? pricePerDeviceUpgradeTokenUSD : pricePerSubscriptionTokenUSD;
    const total = count * unitPrice;

    const hint = document.getElementById('buy-token-rule-hint');
    if (hint) {
        hint.innerText = type === 'DeviceUpgrade'
            ? 'Rule: 1 Device Upgrade Token = +1 Hardware Capacity Slot.'
            : 'Rule: 1 Subscription Token = 1 Device per Month.';
    }

    document.getElementById('buy-token-total').innerText = `$${total.toFixed(2)} USD ($${unitPrice.toFixed(2)} / token)`;
}

async function handleRedeemVoucher(e) {
    e.preventDefault();
    if (!hasPermission('ManageSubscriptions')) {
        alert('Permission Denied: You do not have the ManageSubscriptions permission.');
        return;
    }

    const code = document.getElementById('voucher-code-input').value.trim();
    if (!code) return;

    const btn = document.getElementById('btn-submit-redeem') || e.target.querySelector('button[type="submit"]');
    if (btn) {
        btn.disabled = true;
        btn.innerHTML = '<i class="fa-solid fa-circle-notch fa-spin"></i> Redeeming Voucher...';
    }

    try {
        const res = await fetch('/api/v1/admin/tokens/redeem-master-key', {
            method: 'POST',
            headers: getAuthHeaders({ 'Content-Type': 'application/json' }),
            body: JSON.stringify({ voucherCode: code })
        });

        const data = await res.json();
        if (btn) {
            btn.disabled = false;
            btn.innerHTML = '<i class="fa-solid fa-key"></i> Redeem Tokens';
        }

        if (res.ok && data.success) {
            closeModal('redeem-voucher-modal');
            await loadTokenLedgerStatus();
            alert(data.message || `Token Master Key redeemed successfully! Added ${data.tokensAdded || ''} ${data.tokenType || ''} tokens.`);
        } else {
            alert('Redemption Failed: ' + (data.message || 'Invalid voucher code.'));
        }
    } catch (err) {
        if (btn) {
            btn.disabled = false;
            btn.innerHTML = '<i class="fa-solid fa-key"></i> Redeem Tokens';
        }
        alert('Redemption Error: ' + err.message);
    }
}

async function handleBuyTokens(e) {
    e.preventDefault();
    if (!hasPermission('ManageSubscriptions')) {
        alert('Permission Denied: You do not have the ManageSubscriptions permission.');
        return;
    }

    const type = document.getElementById('buy-token-type')?.value || 'Subscription';
    const count = parseInt(document.getElementById('buy-token-count').value);
    const email = document.getElementById('buy-token-email').value.trim();

    try {
        const res = await fetch('/api/v1/admin/tokens/purchase-paynow', {
            method: 'POST',
            headers: getAuthHeaders({ 'Content-Type': 'application/json' }),
            body: JSON.stringify({ tokenCount: count, tokenType: type, buyerEmail: email })
        });

        if (res.ok) {
            const data = await res.json();
            closeModal('buy-tokens-modal');
            alert(`PayNow Token Transaction Initialized!\nPackage: ${data.tokenType || type}\nRef: ${data.payNowReference}\nAmount: $${data.totalPriceUSD} USD\n\nRedirecting to PayNow checkout...`);
            if (data.payNowRedirectUrl) {
                window.open(data.payNowRedirectUrl, '_blank');
            }
        } else {
            const err = await res.json();
            alert('Token Purchase Error: ' + err.message);
        }
    } catch (err) {
        alert('Token Purchase Error: ' + err.message);
    }
}
