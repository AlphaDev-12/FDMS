const API_BASE = '/api/v1/admin';

let sessionToken = localStorage.getItem('ba_session_token') || '';
let currentUser = null;
try {
  currentUser = JSON.parse(localStorage.getItem('ba_current_user') || 'null');
} catch (e) {
  currentUser = null;
}

let currentPricePerToken = 5.00;
let currentPricePerDeviceUpgradeToken = 50.00;
let cachedFirms = [];
let cachedUsers = [];

document.addEventListener('DOMContentLoaded', () => {
  checkAuthStatus();
});

async function authenticatedFetch(url, options = {}) {
  options.headers = options.headers || {};
  if (sessionToken) {
    options.headers['X-Session-Token'] = sessionToken;
  }

  try {
    const res = await fetch(url, options);
    if (res.status === 401) {
      handleSessionExpired('Session expired or unauthorized. Please log in again.');
    }
    return res;
  } catch (err) {
    console.error('Fetch error:', err);
    throw err;
  }
}

function handleSessionExpired(message) {
  sessionToken = '';
  currentUser = null;
  localStorage.removeItem('ba_session_token');
  localStorage.removeItem('ba_current_user');
  
  const errDiv = document.getElementById('login-error');
  if (errDiv) {
    errDiv.innerText = message;
    errDiv.style.display = 'block';
  }
  document.getElementById('login-overlay').classList.add('active');
}

async function checkAuthStatus() {
  if (!sessionToken) {
    document.getElementById('login-overlay').classList.add('active');
    return;
  }

  try {
    const res = await fetch(`${API_BASE}/auth/me`, {
      headers: { 'X-Session-Token': sessionToken }
    });

    if (res.ok) {
      currentUser = await res.json();
      localStorage.setItem('ba_current_user', JSON.stringify(currentUser));
      updateUserToolbar();
      document.getElementById('login-overlay').classList.remove('active');
      await initDashboard();
    } else {
      handleSessionExpired('Please log in to access FDMS Bridge Admin.');
    }
  } catch (err) {
    document.getElementById('login-overlay').classList.add('active');
  }
}

function updateUserToolbar() {
  if (!currentUser) return;

  const initial = (currentUser.fullName || currentUser.username || 'A').charAt(0).toUpperCase();
  document.getElementById('user-avatar-initial').innerText = initial;
  document.getElementById('user-display-name').innerText = currentUser.fullName || currentUser.username;
  document.getElementById('user-display-role').innerText = currentUser.role || 'Staff';

  // Check permissions for showing User Management tab
  const canManageUsers = hasPermission('ManageUsers');
  const userTab = document.getElementById('tab-users');
  if (userTab) {
    userTab.style.display = canManageUsers ? 'inline-block' : 'none';
  }
}

function hasPermission(perm) {
  if (!currentUser) return false;
  if (currentUser.role === 'SuperAdmin') return true;
  if (!currentUser.permissions) return false;
  return currentUser.permissions.includes('SuperAdmin') || currentUser.permissions.includes('*') || currentUser.permissions.includes(perm);
}

async function handleLoginSubmit(e) {
  e.preventDefault();
  const btn = document.getElementById('btn-login-submit');
  const errDiv = document.getElementById('login-error');
  errDiv.style.display = 'none';

  const req = {
    username: document.getElementById('login-username').value.trim(),
    password: document.getElementById('login-password').value
  };

  btn.disabled = true;
  btn.innerText = 'Authenticating...';

  try {
    const res = await fetch(`${API_BASE}/auth/login`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req)
    });

    if (res.ok) {
      const data = await res.json();
      sessionToken = data.token;
      currentUser = {
        username: data.username,
        fullName: data.fullName,
        role: data.role,
        permissions: data.permissions
      };

      localStorage.setItem('ba_session_token', sessionToken);
      localStorage.setItem('ba_current_user', JSON.stringify(currentUser));

      updateUserToolbar();
      document.getElementById('login-overlay').classList.remove('active');
      document.getElementById('login-password').value = '';
      await initDashboard();
    } else {
      const err = await res.json();
      errDiv.innerText = err.message || 'Invalid login credentials.';
      errDiv.style.display = 'block';
    }
  } catch (err) {
    errDiv.innerText = 'Network error attempting login. Please try again.';
    errDiv.style.display = 'block';
  } finally {
    btn.disabled = false;
    btn.innerText = 'Sign In to Master Portal';
  }
}

function handleLogout() {
  sessionToken = '';
  currentUser = null;
  localStorage.removeItem('ba_session_token');
  localStorage.removeItem('ba_current_user');
  document.getElementById('login-overlay').classList.add('active');
}

async function initDashboard() {
  await loadPricePerToken();
  await loadFirms();
  await loadVouchers();
  await loadTelemetryLogs();
  if (hasPermission('ManageUsers')) {
    await loadUsers();
  }
}

function switchTab(tabId) {
  document.querySelectorAll('.nav-tab').forEach(b => b.classList.remove('active'));
  document.querySelectorAll('.panel').forEach(p => p.classList.remove('active'));

  const tabBtn = document.getElementById(`tab-${tabId}`);
  if (tabBtn) tabBtn.classList.add('active');

  const panel = document.getElementById(`panel-${tabId}`);
  if (panel) panel.classList.add('active');

  if (tabId === 'users' && hasPermission('ManageUsers')) {
    loadUsers();
  }
}

async function loadPricePerToken() {
  try {
    const res = await authenticatedFetch(`${API_BASE}/tokens/price`);
    if (res && res.ok) {
      const data = await res.json();
      currentPricePerToken = data.pricePerTokenUSD || 5.00;
      currentPricePerDeviceUpgradeToken = data.pricePerDeviceUpgradeTokenUSD || 50.00;

      const inputSub = document.getElementById('input-token-price');
      if (inputSub) inputSub.value = currentPricePerToken.toFixed(2);

      const inputUpg = document.getElementById('input-upg-token-price');
      if (inputUpg) inputUpg.value = currentPricePerDeviceUpgradeToken.toFixed(2);
    }
  } catch (err) {
    console.error('Failed to load token prices:', err);
  }
}

async function saveTokenPrices() {
  const subPrice = parseFloat(document.getElementById('input-token-price').value);
  const upgPrice = parseFloat(document.getElementById('input-upg-token-price').value);

  if (isNaN(subPrice) || subPrice <= 0 || isNaN(upgPrice) || upgPrice <= 0) {
    alert('Please enter valid prices greater than $0.');
    return;
  }

  try {
    const res = await authenticatedFetch(`${API_BASE}/tokens/price`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        pricePerTokenUSD: subPrice,
        pricePerDeviceUpgradeTokenUSD: upgPrice
      })
    });
    if (res && res.ok) {
      const data = await res.json();
      currentPricePerToken = data.pricePerTokenUSD;
      currentPricePerDeviceUpgradeToken = data.pricePerDeviceUpgradeTokenUSD;
      alert(data.message);
    } else if (res) {
      const err = await res.json();
      alert('Failed to update token price: ' + (err.message || 'Permission denied'));
    }
  } catch (err) {
    alert('Error updating token price: ' + err.message);
  }
}

function saveTokenPrice() {
  return saveTokenPrices();
}

async function loadFirms() {
  try {
    const res = await authenticatedFetch(`${API_BASE}/firms`);
    if (!res || !res.ok) return;

    cachedFirms = await res.json();
    renderFirmsTable(cachedFirms);
    updateMetrics(cachedFirms);
  } catch (err) {
    console.error('Failed to load firms:', err);
  }
}

function renderFirmsTable(firms) {
  const tbody = document.getElementById('firms-table-body');
  tbody.innerHTML = '';

  if (!firms || firms.length === 0) {
    tbody.innerHTML = `<tr><td colspan="11" style="text-align: center; color: var(--text-muted);">No consultancy firms registered yet.</td></tr>`;
    return;
  }

  firms.forEach(f => {
    const lastHeartbeat = f.lastHeartbeatAt ? new Date(f.lastHeartbeatAt).toLocaleString() : 'Never';
    const isTampered = f.immutabilityStatus === 'TAMPER_DETECTED';

    let unlimitedBadge = '<span class="badge badge-inactive">🎟️ Standard</span>';
    if (f.unlimitedTier === 'Completely') {
      unlimitedBadge = '<span class="badge badge-active" style="background: rgba(16, 185, 129, 0.2); color: #34d399; border: 1px solid rgba(52, 211, 153, 0.4);">⚡ Unlimited</span>';
    } else if (f.unlimitedTier === 'Annual') {
      if (f.isUnlimitedActive) {
        const expStr = f.unlimitedExpiresAt ? new Date(f.unlimitedExpiresAt).toLocaleDateString() : '';
        unlimitedBadge = `<span class="badge badge-active" style="background: rgba(59, 130, 246, 0.2); color: #60a5fa; border: 1px solid rgba(96, 165, 250, 0.4);" title="Expires ${expStr}">📅 Annual (${expStr})</span>`;
      } else {
        unlimitedBadge = '<span class="badge badge-tamper" style="background: rgba(239, 68, 68, 0.2); color: #f87171;">⚠️ Annual Expired</span>';
      }
    }

    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td><strong>${escapeHtml(f.firmName)}</strong></td>
      <td>${escapeHtml(f.contactEmail)}</td>
      <td><code style="background: rgba(0,0,0,0.3); padding: 0.2rem 0.4rem; border-radius: 4px; color: #818cf8;">${escapeHtml(f.firmApiKey)}</code></td>
      <td>${f.totalTokensPurchased} <span style="font-size: 0.75rem; color: var(--text-muted);">(${f.totalSubscriptionTokensPurchased || f.totalTokensPurchased}🎟️ / ${f.totalDeviceUpgradeTokensPurchased || 0}⚡)</span></td>
      <td>${f.totalTokensConsumed}</td>
      <td><strong style="color: var(--secondary-accent);">${f.currentTokenBalance}</strong></td>
      <td>${unlimitedBadge}</td>
      <td>
        <span class="badge ${isTampered ? 'badge-tamper' : 'badge-active'}">
          ${isTampered ? '⚠️ TAMPER DETECTED' : '✓ OK'}
        </span>
      </td>
      <td style="font-size: 0.8rem; color: var(--text-muted);">${lastHeartbeat}</td>
      <td>
        <span class="badge ${f.isActive ? 'badge-active' : 'badge-inactive'}">
          ${f.isActive ? 'Active' : 'Disabled'}
        </span>
      </td>
      <td>
        <div style="display: flex; gap: 0.4rem;">
          <button class="btn btn-secondary" style="padding: 0.3rem 0.6rem; font-size: 0.75rem;" onclick="openEditUnlimitedModal('${f.id}')">
            Configure Unlimited
          </button>
          <button class="btn btn-secondary" style="padding: 0.3rem 0.6rem; font-size: 0.75rem;" onclick="toggleFirmStatus('${f.id}')">
            ${f.isActive ? 'Disable' : 'Enable'}
          </button>
        </div>
      </td>
    `;
    tbody.appendChild(tr);
  });
}

function updateMetrics(firms) {
  const firmsElem = document.getElementById('metric-firms-count');
  if (firmsElem) firmsElem.innerText = firms ? firms.length : 0;

  let subTokens = 0;
  let upgTokens = 0;
  let tamperAlerts = 0;

  if (firms) {
    firms.forEach(f => {
      if (f.totalSubscriptionTokensPurchased !== undefined || f.totalDeviceUpgradeTokensPurchased !== undefined) {
        subTokens += f.totalSubscriptionTokensPurchased || 0;
        upgTokens += f.totalDeviceUpgradeTokensPurchased || 0;
      } else {
        subTokens += f.totalTokensPurchased || 0;
      }
      if (f.immutabilityStatus === 'TAMPER_DETECTED') tamperAlerts++;
    });
  }

  if (window.cachedVouchers && window.cachedVouchers.length > 0) {
    let vSub = 0;
    let vUpg = 0;
    window.cachedVouchers.forEach(v => {
      if (v.tokenType === 'DeviceUpgrade') vUpg += (v.tokenCount || 0);
      else vSub += (v.tokenCount || 0);
    });
    if (vSub > subTokens) subTokens = vSub;
    if (vUpg > upgTokens) upgTokens = vUpg;
  }

  const subElem = document.getElementById('metric-sub-tokens-issued');
  if (subElem) subElem.innerText = subTokens.toLocaleString();

  const upgElem = document.getElementById('metric-upg-tokens-issued');
  if (upgElem) upgElem.innerText = upgTokens.toLocaleString();

  const totalRev = (subTokens * currentPricePerToken) + (upgTokens * currentPricePerDeviceUpgradeToken);
  const revElem = document.getElementById('metric-revenue');
  if (revElem) revElem.innerText = `$${totalRev.toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;

  const tamperElem = document.getElementById('metric-tamper-alerts');
  if (tamperElem) tamperElem.innerText = tamperAlerts;
}

async function toggleFirmStatus(id) {
  try {
    const res = await authenticatedFetch(`${API_BASE}/firms/${id}/toggle-status`, { method: 'PUT' });
    if (res && res.ok) loadFirms();
  } catch (err) {
    alert('Failed to toggle firm status.');
  }
}

async function loadVouchers() {
  try {
    const res = await authenticatedFetch(`${API_BASE}/tokens/vouchers`);
    if (!res || !res.ok) return;

    const vouchers = await res.json();
    window.cachedVouchers = vouchers;
    if (cachedFirms && cachedFirms.length > 0) {
      updateMetrics(cachedFirms);
    }

    const tbody = document.getElementById('vouchers-table-body');
    tbody.innerHTML = '';

    if (!vouchers || vouchers.length === 0) {
      tbody.innerHTML = `<tr><td colspan="9" style="text-align: center; color: var(--text-muted);">No token vouchers issued yet.</td></tr>`;
      return;
    }

    vouchers.forEach(v => {
      const genAt = new Date(v.generatedAt).toLocaleString();
      const redAt = v.redeemedAt ? new Date(v.redeemedAt).toLocaleString() : '-';

      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td><strong>${escapeHtml(v.firmName)}</strong></td>
        <td><code style="font-size: 0.75rem; color: #22d3ee; word-break: break-all;">${escapeHtml(v.voucherCode)}</code></td>
        <td><strong>${v.tokenCount}</strong></td>
        <td>$${v.unitPriceUSD.toFixed(2)}</td>
        <td>$${v.totalPriceUSD.toFixed(2)}</td>
        <td>${v.purchasedViaPayNow ? 'PayNow Online' : 'Manual Admin'}</td>
        <td>
          <span class="badge ${v.status === 'Redeemed' ? 'badge-active' : 'badge-inactive'}">
            ${v.status}
          </span>
        </td>
        <td style="font-size: 0.8rem;">${genAt}</td>
        <td style="font-size: 0.8rem;">${redAt}</td>
      `;
      tbody.appendChild(tr);
    });
  } catch (err) {
    console.error('Failed to load vouchers:', err);
  }
}

async function loadTelemetryLogs() {
  try {
    const res = await authenticatedFetch(`/api/v1/telemetry/logs`);
    if (!res || !res.ok) return;

    const logs = await res.json();
    const tbody = document.getElementById('telemetry-table-body');
    tbody.innerHTML = '';

    if (!logs || logs.length === 0) {
      tbody.innerHTML = `<tr><td colspan="9" style="text-align: center; color: var(--text-muted);">No 24H telemetry heartbeats recorded yet.</td></tr>`;
      return;
    }

    logs.forEach(l => {
      const repAt = new Date(l.reportedAt).toLocaleString();
      const isTamper = l.immutabilityStatus === 'TAMPER_DETECTED';

      const tr = document.createElement('tr');
      tr.innerHTML = `
        <td style="font-size: 0.8rem;">${repAt}</td>
        <td><strong>${escapeHtml(l.firmName)}</strong></td>
        <td>${l.totalTokensPurchased}</td>
        <td>${l.totalTokensConsumed}</td>
        <td><strong>${l.currentTokenBalance}</strong></td>
        <td>${l.activeClientSubscriptions}</td>
        <td><span class="badge badge-inactive">${escapeHtml(l.logType)}</span></td>
        <td>
          <span class="badge ${isTamper ? 'badge-tamper' : 'badge-active'}">
            ${isTamper ? `⚠️ TAMPER ERROR: ${escapeHtml(l.tamperErrorMessage || '')}` : '✓ OK'}
          </span>
        </td>
        <td style="font-size: 0.8rem; color: var(--text-muted);">${escapeHtml(l.ipAddress)}</td>
      `;
      tbody.appendChild(tr);
    });
  } catch (err) {
    console.error('Failed to load telemetry logs:', err);
  }
}

/* User Management Functions */
async function loadUsers() {
  try {
    const res = await authenticatedFetch(`${API_BASE}/users`);
    if (!res || !res.ok) return;

    cachedUsers = await res.json();
    filterUsers();
  } catch (err) {
    console.error('Failed to load users:', err);
  }
}

function filterUsers() {
  const query = (document.getElementById('user-search-input')?.value || '').toLowerCase();
  const roleFilter = document.getElementById('user-role-filter')?.value || '';

  const filtered = cachedUsers.filter(u => {
    const matchQuery = !query || 
      (u.username && u.username.toLowerCase().includes(query)) ||
      (u.fullName && u.fullName.toLowerCase().includes(query)) ||
      (u.email && u.email.toLowerCase().includes(query));
    
    const matchRole = !roleFilter || u.role === roleFilter;

    return matchQuery && matchRole;
  });

  renderUsersTable(filtered);
}

function renderUsersTable(users) {
  const tbody = document.getElementById('users-table-body');
  if (!tbody) return;

  tbody.innerHTML = '';

  if (!users || users.length === 0) {
    tbody.innerHTML = `<tr><td colspan="7" style="text-align: center; color: var(--text-muted);">No admin users found matching criteria.</td></tr>`;
    return;
  }

  users.forEach(u => {
    const lastLogin = u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : 'Never';
    
    let roleClass = 'badge-role-staff';
    if (u.role === 'SuperAdmin') roleClass = 'badge-role-super';
    else if (u.role === 'Admin') roleClass = 'badge-role-admin';

    const permPills = (u.permissions || []).map(p => `<span class="perm-pill">${escapeHtml(p)}</span>`).join('');

    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td>
        <div><strong>${escapeHtml(u.fullName)}</strong></div>
        <small style="color: var(--text-muted); font-size: 0.75rem;">@${escapeHtml(u.username)}</small>
      </td>
      <td>${escapeHtml(u.email)}</td>
      <td><span class="badge ${roleClass}">${escapeHtml(u.role)}</span></td>
      <td>${permPills || '<span style="color: var(--text-muted); font-size: 0.75rem;">None</span>'}</td>
      <td style="font-size: 0.8rem; color: var(--text-muted);">${lastLogin}</td>
      <td>
        <span class="badge ${u.isActive ? 'badge-active' : 'badge-inactive'}">
          ${u.isActive ? 'Active' : 'Disabled'}
        </span>
      </td>
      <td>
        <div style="display: flex; gap: 0.5rem;">
          <button class="btn btn-secondary" style="padding: 0.3rem 0.6rem; font-size: 0.75rem;" onclick="openEditUserModal('${u.id}')">
            Edit Permissions
          </button>
          <button class="btn btn-logout" style="padding: 0.3rem 0.6rem; font-size: 0.75rem;" onclick="deleteUserById('${u.id}', '${escapeHtml(u.username)}')">
            Delete
          </button>
        </div>
      </td>
    `;
    tbody.appendChild(tr);
  });
}

function openCreateUserModal() {
  document.getElementById('create-user-username').value = '';
  document.getElementById('create-user-fullname').value = '';
  document.getElementById('create-user-email').value = '';
  document.getElementById('create-user-password').value = '';
  document.getElementById('create-user-role').value = 'Staff';
  
  ['ManageFirms', 'ManageTokens', 'ManageSettings', 'ManageUsers'].forEach(p => {
    const chk = document.getElementById(`perm-create-${p}`);
    if (chk) chk.checked = false;
  });

  document.getElementById('modal-create-user').classList.add('active');
}

function onUserRoleChange(mode) {
  const roleSelect = document.getElementById(`${mode}-user-role`);
  if (!roleSelect) return;
  const role = roleSelect.value;

  const permKeys = ['ManageFirms', 'ManageTokens', 'ManageSettings', 'ManageUsers'];
  if (role === 'SuperAdmin') {
    permKeys.forEach(p => {
      const chk = document.getElementById(`perm-${mode}-${p}`);
      if (chk) chk.checked = true;
    });
  } else if (role === 'Admin') {
    ['ManageFirms', 'ManageTokens', 'ManageSettings'].forEach(p => {
      const chk = document.getElementById(`perm-${mode}-${p}`);
      if (chk) chk.checked = true;
    });
    const muChk = document.getElementById(`perm-${mode}-ManageUsers`);
    if (muChk) muChk.checked = false;
  } else if (role === 'Staff') {
    permKeys.forEach(p => {
      const chk = document.getElementById(`perm-${mode}-${p}`);
      if (chk) chk.checked = false;
    });
  }
}

async function handleCreateUser(e) {
  e.preventDefault();

  const permKeys = ['ManageFirms', 'ManageTokens', 'ManageSettings', 'ManageUsers'];
  const permissions = permKeys.filter(p => {
    const chk = document.getElementById(`perm-create-${p}`);
    return chk && chk.checked;
  });

  const req = {
    username: document.getElementById('create-user-username').value.trim(),
    fullName: document.getElementById('create-user-fullname').value.trim(),
    email: document.getElementById('create-user-email').value.trim(),
    password: document.getElementById('create-user-password').value,
    role: document.getElementById('create-user-role').value,
    permissions: permissions
  };

  try {
    const res = await authenticatedFetch(`${API_BASE}/users`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req)
    });

    if (res && res.ok) {
      closeModal('modal-create-user');
      await loadUsers();
      alert('Admin User created successfully!');
    } else if (res) {
      const err = await res.json();
      alert('Error creating user: ' + (err.message || 'Validation failed'));
    }
  } catch (err) {
    alert('Error creating user: ' + err.message);
  }
}

function openEditUserModal(userId) {
  const u = cachedUsers.find(x => x.id === userId);
  if (!u) return;

  document.getElementById('edit-user-id').value = u.id;
  document.getElementById('edit-user-username').value = u.username;
  document.getElementById('edit-user-fullname').value = u.fullName;
  document.getElementById('edit-user-email').value = u.email;
  document.getElementById('edit-user-password').value = '';
  document.getElementById('edit-user-role').value = u.role || 'Staff';
  document.getElementById('edit-user-status').value = u.isActive ? 'true' : 'false';

  const userPerms = u.permissions || [];
  ['ManageFirms', 'ManageTokens', 'ManageSettings', 'ManageUsers'].forEach(p => {
    const chk = document.getElementById(`perm-edit-${p}`);
    if (chk) {
      chk.checked = userPerms.includes('SuperAdmin') || userPerms.includes('*') || userPerms.includes(p);
    }
  });

  document.getElementById('modal-edit-user').classList.add('active');
}

async function handleEditUserSubmit(e) {
  e.preventDefault();

  const userId = document.getElementById('edit-user-id').value;
  const permKeys = ['ManageFirms', 'ManageTokens', 'ManageSettings', 'ManageUsers'];
  const permissions = permKeys.filter(p => {
    const chk = document.getElementById(`perm-edit-${p}`);
    return chk && chk.checked;
  });

  const req = {
    fullName: document.getElementById('edit-user-fullname').value.trim(),
    email: document.getElementById('edit-user-email').value.trim(),
    password: document.getElementById('edit-user-password').value || null,
    role: document.getElementById('edit-user-role').value,
    isActive: document.getElementById('edit-user-status').value === 'true',
    permissions: permissions
  };

  try {
    const res = await authenticatedFetch(`${API_BASE}/users/${userId}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req)
    });

    if (res && res.ok) {
      closeModal('modal-edit-user');
      await loadUsers();
      alert('Admin User updated successfully!');
    } else if (res) {
      const err = await res.json();
      alert('Error updating user: ' + (err.message || 'Update failed'));
    }
  } catch (err) {
    alert('Error updating user: ' + err.message);
  }
}

async function handleDeleteUserFromModal() {
  const userId = document.getElementById('edit-user-id').value;
  const username = document.getElementById('edit-user-username').value;
  await deleteUserById(userId, username);
}

async function deleteUserById(userId, username) {
  if (!confirm(`Are you sure you want to delete administrator user '@${username}'? This action cannot be undone.`)) {
    return;
  }

  try {
    const res = await authenticatedFetch(`${API_BASE}/users/${userId}`, {
      method: 'DELETE'
    });

    if (res && res.ok) {
      closeModal('modal-edit-user');
      await loadUsers();
      alert(`User '@${username}' removed successfully.`);
    } else if (res) {
      const err = await res.json();
      alert('Failed to delete user: ' + (err.message || 'Error occurred'));
    }
  } catch (err) {
    alert('Error deleting user: ' + err.message);
  }
}

function openRegisterFirmModal() {
  document.getElementById('modal-register-firm').classList.add('active');
}

function openGenerateTokenModal() {
  const select = document.getElementById('gen-firm-select');
  select.innerHTML = '';

  cachedFirms.forEach(f => {
    const opt = document.createElement('option');
    opt.value = f.id;
    opt.textContent = `${f.firmName} (${f.contactEmail})`;
    select.appendChild(opt);
  });

  calculateGenTotal();
  document.getElementById('modal-generate-token').classList.add('active');
}

function closeModal(id) {
  document.getElementById(id).classList.remove('active');
}

function closeModalOnBackdrop(e, id) {
  if (e && e.target && e.target.classList.contains('modal-overlay')) {
    closeModal(id);
  }
}

document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') {
    document.querySelectorAll('.modal-overlay.active').forEach(m => m.classList.remove('active'));
  }
});

function calculateGenTotal() {
  const count = parseInt(document.getElementById('gen-token-count').value) || 0;
  const tokenType = document.getElementById('gen-token-type')?.value || 'Subscription';
  const unitPrice = tokenType === 'DeviceUpgrade' ? currentPricePerDeviceUpgradeToken : currentPricePerToken;
  const total = count * unitPrice;
  document.getElementById('gen-total-cost').innerText = `$${total.toFixed(2)} USD ($${unitPrice.toFixed(2)} / token)`;
}

function toggleUnlimitedDateInput(prefix) {
  const tier = document.getElementById(`${prefix}-firm-unlimited-tier` || `${prefix}-unlimited-tier`)?.value || document.getElementById(`${prefix}-unlimited-tier`)?.value;
  const group = document.getElementById(`${prefix}-firm-unlimited-date-group` || `${prefix}-unlimited-date-group`) || document.getElementById(`${prefix}-unlimited-date-group`);
  if (group) {
    group.style.display = tier === 'Annual' ? 'block' : 'none';
  }
}

async function handleRegisterFirm(e) {
  e.preventDefault();
  const tier = document.getElementById('reg-firm-unlimited-tier').value;
  const rawDate = document.getElementById('reg-firm-unlimited-date').value;

  const req = {
    firmName: document.getElementById('reg-firm-name').value,
    contactEmail: document.getElementById('reg-firm-email').value,
    contactPhone: document.getElementById('reg-firm-phone').value,
    taxPayerTIN: document.getElementById('reg-firm-tin').value,
    unlimitedTier: tier,
    unlimitedExpiresAt: (tier === 'Annual' && rawDate) ? new Date(rawDate).toISOString() : null
  };

  try {
    const res = await authenticatedFetch(`${API_BASE}/firms`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req)
    });

    if (res && res.ok) {
      closeModal('modal-register-firm');
      await loadFirms();
      alert('Consultancy Firm registered successfully!');
    } else if (res) {
      const err = await res.json();
      alert('Error registering firm: ' + err.message);
    }
  } catch (err) {
    alert('Error registering firm: ' + err.message);
  }
}

function openEditUnlimitedModal(firmId) {
  const firm = globalFirmsData.find(f => f.id === firmId);
  if (!firm) return;

  document.getElementById('edit-unlimited-firm-id').value = firm.id;
  document.getElementById('edit-unlimited-firm-name').value = firm.firmName;
  document.getElementById('edit-unlimited-tier').value = firm.unlimitedTier || 'None';
  
  if (firm.unlimitedExpiresAt) {
    const d = new Date(firm.unlimitedExpiresAt);
    const dateStr = d.toISOString().split('T')[0];
    document.getElementById('edit-unlimited-date').value = dateStr;
  } else {
    document.getElementById('edit-unlimited-date').value = '';
  }

  toggleUnlimitedDateInput('edit');
  openModal('modal-edit-unlimited');
}

async function handleUpdateUnlimitedSubmit(e) {
  e.preventDefault();
  const firmId = document.getElementById('edit-unlimited-firm-id').value;
  const tier = document.getElementById('edit-unlimited-tier').value;
  const rawDate = document.getElementById('edit-unlimited-date').value;

  const req = {
    unlimitedTier: tier,
    unlimitedExpiresAt: (tier === 'Annual' && rawDate) ? new Date(rawDate).toISOString() : null
  };

  try {
    const res = await authenticatedFetch(`${API_BASE}/firms/${firmId}/unlimited-tier`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req)
    });

    if (res && res.ok) {
      closeModal('modal-edit-unlimited');
      await loadFirms();
      alert('Unlimited Privilege Tier updated successfully!');
    } else if (res) {
      const err = await res.json();
      alert('Error updating unlimited tier: ' + err.message);
    }
  } catch (err) {
    alert('Error updating unlimited tier: ' + err.message);
  }
}

async function handleGenerateToken(e) {
  e.preventDefault();
  const req = {
    consultancyFirmId: document.getElementById('gen-firm-select').value,
    tokenCount: parseInt(document.getElementById('gen-token-count').value),
    tokenType: document.getElementById('gen-token-type')?.value || 'Subscription'
  };

  try {
    const res = await authenticatedFetch(`${API_BASE}/tokens/generate`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(req)
    });

    if (res && res.ok) {
      closeModal('modal-generate-token');
      await loadVouchers();
      await loadFirms();
      alert('Token Master Key generated successfully!');
    } else if (res) {
      const err = await res.json();
      alert('Error generating token: ' + err.message);
    }
  } catch (err) {
    alert('Error generating token: ' + err.message);
  }
}

function escapeHtml(str) {
  if (!str) return '';
  return str.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
