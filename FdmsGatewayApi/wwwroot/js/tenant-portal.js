// Tenant Portal JavaScript Logic

let currentSession = null;
let currentDocs = [];

document.addEventListener('DOMContentLoaded', () => {
    checkActiveSession();
});

function checkActiveSession() {
    const savedToken = localStorage.getItem('tenantToken');
    if (savedToken) {
        fetch('/api/v1/tenant/auth/me', {
            headers: { 'X-Tenant-Session': savedToken }
        })
        .then(res => {
            if (!res.ok) throw new Error('Session expired');
            return res.json();
        })
        .then(data => {
            currentSession = {
                token: savedToken,
                user: data.user,
                businessName: data.businessName,
                tenantApiKey: data.tenantApiKey
            };
            onLoginSuccess();
        })
        .catch(err => {
            console.warn('Session invalid:', err);
            localStorage.removeItem('tenantToken');
            showLoginModal();
        });
    } else {
        showLoginModal();
    }
}

function showLoginModal() {
    document.getElementById('loginModal').classList.add('active');
    document.getElementById('userNav').style.display = 'none';
}

function handleLogin(event) {
    event.preventDefault();
    const email = document.getElementById('loginEmail').value.trim();
    const password = document.getElementById('loginPassword').value;
    const apiKey = document.getElementById('loginTenantApiKey').value.trim();
    const errDiv = document.getElementById('loginError');

    errDiv.style.display = 'none';

    fetch('/api/v1/tenant/auth/login', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
            email: email,
            password: password,
            tenantApiKey: apiKey || null
        })
    })
    .then(res => res.json().then(data => ({ status: res.status, body: data })))
    .then(res => {
        if (!res.body.success) {
            errDiv.innerText = res.body.message || 'Authentication failed.';
            errDiv.style.display = 'block';
            return;
        }

        currentSession = {
            token: res.body.token,
            user: res.body.user,
            businessName: res.body.businessName,
            tenantId: res.body.tenantId
        };

        localStorage.setItem('tenantToken', res.body.token);
        document.getElementById('loginModal').classList.remove('active');
        onLoginSuccess();
    })
    .catch(err => {
        errDiv.innerText = 'Network error authenticating user.';
        errDiv.style.display = 'block';
    });
}

function onLoginSuccess() {
    document.getElementById('headerTenantName').innerText = currentSession.businessName;
    document.getElementById('userName').innerText = currentSession.user.fullName;
    document.getElementById('userRole').innerText = currentSession.user.role;
    document.getElementById('userAvatar').innerText = currentSession.user.fullName.charAt(0).toUpperCase();
    document.getElementById('userNav').style.display = 'flex';

    // Show Users Tab if has permission
    const hasUsersPerm = currentSession.user.role === 'TenantAdmin' || (currentSession.user.permissions && currentSession.user.permissions.includes('ManageUsers'));
    document.getElementById('usersTabBtn').style.display = hasUsersPerm ? 'flex' : 'none';

    loadSummary();
    loadDocuments();
    loadDevices();
    if (hasUsersPerm) loadUsers();
}

function logoutTenant() {
    localStorage.removeItem('tenantToken');
    currentSession = null;
    document.getElementById('loginModal').classList.add('active');
    document.getElementById('userNav').style.display = 'none';
}

function switchTab(tabName) {
    document.querySelectorAll('.tab-btn').forEach(btn => btn.classList.remove('active'));
    event.currentTarget.classList.add('active');

    document.getElementById('tabOverview').style.display = tabName === 'overview' ? 'block' : 'none';
    document.getElementById('tabDocuments').style.display = tabName === 'documents' ? 'block' : 'none';
    document.getElementById('tabDevices').style.display = tabName === 'devices' ? 'block' : 'none';
    document.getElementById('tabUsers').style.display = tabName === 'users' ? 'block' : 'none';
}

function loadSummary() {
    fetch('/api/v1/tenant/dashboard/summary', {
        headers: { 'X-Tenant-Session': currentSession.token }
    })
    .then(res => res.json())
    .then(data => {
        document.getElementById('kpiUsdSales').innerText = '$' + (data.todayUsdSales || 0).toFixed(2);
        document.getElementById('kpiZigSales').innerText = 'ZiG ' + (data.todayZigSales || 0).toFixed(2);
        document.getElementById('kpiTotalDocs').innerText = data.totalDocuments || 0;
        document.getElementById('kpiDocBreakdown').innerText = `${data.totalFiscalized || 0} Synced / ${data.totalPending || 0} Pending`;
        document.getElementById('kpiActiveDevices').innerText = data.registeredDevicesCount || 0;
    })
    .catch(err => console.error('Error loading tenant summary:', err));
}

function loadDocuments() {
    fetch('/api/v1/tenant/dashboard/documents?pageSize=50', {
        headers: { 'X-Tenant-Session': currentSession.token }
    })
    .then(res => res.json())
    .then(data => {
        currentDocs = data.items || [];
        renderDocumentsTable(currentDocs);
        renderOverviewLedger(currentDocs.slice(0, 5));
    })
    .catch(err => console.error('Error loading documents:', err));
}

function renderDocumentsTable(docs) {
    const tbody = document.getElementById('documentsTableBody');
    if (!docs || docs.length === 0) {
        tbody.innerHTML = `<tr><td colspan="6" style="text-align: center; color: var(--text-muted);">No fiscalized receipts found for your tenant account.</td></tr>`;
        return;
    }

    tbody.innerHTML = docs.map(d => `
        <tr>
            <td style="font-weight: 600;">${escapeHtml(d.invoiceNumber)}</td>
            <td><code style="background: rgba(255,255,255,0.05); padding: 2px 6px; border-radius: 4px;">${escapeHtml(d.zimraDeviceId)}</code></td>
            <td>${escapeHtml(d.currencyCode)}</td>
            <td style="font-weight: 700;">${d.currencyCode === 'USD' ? '$' : ''}${Number(d.grossInclusiveAmount).toFixed(2)}</td>
            <td><span class="badge-status ${d.zimraSyncStatus === 'SYNCED' ? 'badge-synced' : 'badge-pending'}">${escapeHtml(d.zimraSyncStatus)}</span></td>
            <td>${new Date(d.createdAt).toLocaleString()}</td>
        </tr>
    `).join('');
}

function renderOverviewLedger(docs) {
    const tbody = document.getElementById('overviewLedgerBody');
    if (!docs || docs.length === 0) {
        tbody.innerHTML = `<tr><td colspan="5" style="text-align: center; color: var(--text-muted);">No recent receipts found.</td></tr>`;
        return;
    }

    tbody.innerHTML = docs.map(d => `
        <tr>
            <td style="font-weight: 600;">${escapeHtml(d.invoiceNumber)}</td>
            <td>${escapeHtml(d.zimraDeviceId)}</td>
            <td style="font-weight: 700;">${d.currencyCode === 'USD' ? '$' : ''}${Number(d.grossInclusiveAmount).toFixed(2)}</td>
            <td><span class="badge-status ${d.zimraSyncStatus === 'SYNCED' ? 'badge-synced' : 'badge-pending'}">${escapeHtml(d.zimraSyncStatus)}</span></td>
            <td>${new Date(d.createdAt).toLocaleTimeString()}</td>
        </tr>
    `).join('');
}

function filterDocs() {
    const query = document.getElementById('searchDocs').value.toLowerCase().trim();
    if (!query) {
        renderDocumentsTable(currentDocs);
        return;
    }

    const filtered = currentDocs.filter(d =>
        d.invoiceNumber.toLowerCase().includes(query) ||
        d.zimraDeviceId.toLowerCase().includes(query)
    );
    renderDocumentsTable(filtered);
}

function loadDevices() {
    fetch('/api/v1/tenant/dashboard/devices', {
        headers: { 'X-Tenant-Session': currentSession.token }
    })
    .then(res => res.json())
    .then(devices => {
        const tbody = document.getElementById('devicesTableBody');
        if (!devices || devices.length === 0) {
            tbody.innerHTML = `<tr><td colspan="5" style="text-align: center; color: var(--text-muted);">No registered ZIMRA devices found.</td></tr>`;
            return;
        }

        tbody.innerHTML = devices.map(dev => `
            <tr>
                <td style="font-weight: 700;">${escapeHtml(dev.zimraDeviceId)}</td>
                <td>Seq #${dev.lastSequenceNumber}</td>
                <td><span class="badge-status ${dev.isActive ? 'badge-synced' : 'badge-failed'}">${dev.isActive ? 'Active' : 'Inactive'}</span></td>
                <td><span style="color: var(--accent-green);"><i class="fa-solid fa-circle-check"></i> X.509 PFX Certificate Loaded</span></td>
                <td>${new Date(dev.createdAt).toLocaleDateString()}</td>
            </tr>
        `).join('');
    })
    .catch(err => console.error('Error loading devices:', err));
}

let cachedUsers = [];

function loadUsers() {
    fetch('/api/v1/tenant/users', {
        headers: { 'X-Tenant-Session': currentSession.token }
    })
    .then(res => res.json())
    .then(users => {
        cachedUsers = users || [];
        const tbody = document.getElementById('usersTableBody');
        if (!users || users.length === 0) {
            tbody.innerHTML = `<tr><td colspan="6" style="text-align: center; color: var(--text-muted);">No team members found.</td></tr>`;
            return;
        }

        tbody.innerHTML = users.map(u => `
            <tr>
                <td style="font-weight: 600;">${escapeHtml(u.fullName)}</td>
                <td>${escapeHtml(u.email)} ${!u.isActive ? '<span class="badge-status badge-failed">Deactivated</span>' : ''}</td>
                <td><span class="badge-tenant">${escapeHtml(u.role)}</span></td>
                <td><small style="color: var(--text-muted);">${(u.permissions || []).join(', ')}</small></td>
                <td>${u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : 'Never'}</td>
                <td>
                    <button class="tab-btn" style="padding: 0.2rem 0.6rem; font-size: 0.75rem; display: inline-flex; border: 1px solid var(--border-color);" onclick="openEditUserModal(${u.id})"><i class="fa-solid fa-user-pen"></i> Permissions</button>
                    ${u.id !== currentSession.user.id ? `<button class="btn-logout" style="padding: 0.2rem 0.6rem; font-size: 0.75rem;" onclick="deleteUser(${u.id})">Remove</button>` : '<small style="color: var(--text-muted); margin-left: 4px;">Active You</small>'}
                </td>
            </tr>
        `).join('');
    })
    .catch(err => console.error('Error loading users:', err));
}

function openCreateUserModal() {
    document.getElementById('createUserModal').classList.add('active');
}

function closeCreateUserModal() {
    document.getElementById('createUserModal').classList.remove('active');
}

function openEditUserModal(userId) {
    const user = cachedUsers.find(u => u.id === userId);
    if (!user) return;

    document.getElementById('editUserId').value = user.id;
    document.getElementById('editUserEmail').value = `${user.fullName} (${user.email})`;
    document.getElementById('editUserRole').value = user.role || 'TenantOperator';
    document.getElementById('editUserActive').value = user.isActive ? 'true' : 'false';

    const perms = user.permissions || [];
    document.getElementById('editPermDashboard').checked = perms.includes('ViewDashboard');
    document.getElementById('editPermDocuments').checked = perms.includes('ViewDocuments');
    document.getElementById('editPermDevices').checked = perms.includes('ManageDevices');
    document.getElementById('editPermUsers').checked = perms.includes('ManageUsers');

    document.getElementById('editUserError').style.display = 'none';
    document.getElementById('editUserModal').classList.add('active');
}

function closeEditUserModal() {
    document.getElementById('editUserModal').classList.remove('active');
}

function handleUpdateUserPermissions(event) {
    event.preventDefault();
    const userId = document.getElementById('editUserId').value;
    const role = document.getElementById('editUserRole').value;
    const isActive = document.getElementById('editUserActive').value === 'true';
    const errDiv = document.getElementById('editUserError');

    const perms = [];
    if (document.getElementById('editPermDashboard').checked) perms.push('ViewDashboard');
    if (document.getElementById('editPermDocuments').checked) perms.push('ViewDocuments');
    if (document.getElementById('editPermDevices').checked) perms.push('ManageDevices');
    if (document.getElementById('editPermUsers').checked) perms.push('ManageUsers');

    errDiv.style.display = 'none';

    fetch(`/api/v1/tenant/users/${userId}/permissions`, {
        method: 'PUT',
        headers: {
            'Content-Type': 'application/json',
            'X-Tenant-Session': currentSession.token
        },
        body: JSON.stringify({
            role: role,
            permissions: perms,
            isActive: isActive
        })
    })
    .then(res => res.json().then(data => ({ status: res.status, body: data })))
    .then(res => {
        if (res.status !== 200) {
            errDiv.innerText = res.body.message || 'Failed to update user permissions.';
            errDiv.style.display = 'block';
            return;
        }

        closeEditUserModal();
        loadUsers();
    })
    .catch(err => {
        errDiv.innerText = 'Error updating user permissions.';
        errDiv.style.display = 'block';
    });
}

function handleCreateUser(event) {
    event.preventDefault();
    const fullName = document.getElementById('newUserFullName').value.trim();
    const email = document.getElementById('newUserEmail').value.trim();
    const password = document.getElementById('newUserPassword').value;
    const role = document.getElementById('newUserRole').value;
    const errDiv = document.getElementById('createUserError');

    const perms = [];
    if (document.getElementById('permDashboard').checked) perms.push('ViewDashboard');
    if (document.getElementById('permDocuments').checked) perms.push('ViewDocuments');
    if (document.getElementById('permDevices').checked) perms.push('ManageDevices');
    if (document.getElementById('permUsers').checked) perms.push('ManageUsers');

    errDiv.style.display = 'none';

    fetch('/api/v1/tenant/users', {
        method: 'POST',
        headers: {
            'Content-Type': 'application/json',
            'X-Tenant-Session': currentSession.token
        },
        body: JSON.stringify({
            fullName,
            email,
            password,
            role,
            permissions: perms
        })
    })
    .then(res => res.json().then(data => ({ status: res.status, body: data })))
    .then(res => {
        if (res.status !== 201) {
            errDiv.innerText = res.body.message || 'Failed to create tenant user.';
            errDiv.style.display = 'block';
            return;
        }

        closeCreateUserModal();
        loadUsers();
    })
    .catch(err => {
        errDiv.innerText = 'Error creating user.';
        errDiv.style.display = 'block';
    });
}

function deleteUser(userId) {
    if (!confirm('Are you sure you want to remove this tenant team member?')) return;

    fetch(`/api/v1/tenant/users/${userId}`, {
        method: 'DELETE',
        headers: { 'X-Tenant-Session': currentSession.token }
    })
    .then(res => res.json())
    .then(data => {
        loadUsers();
    })
    .catch(err => console.error('Error removing user:', err));
}

function escapeHtml(str) {
    if (!str) return '';
    return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
