/**
 * ZIMRA FDMS FISCAL BRIDGE CONSOLE - JS APP
 */

document.addEventListener('DOMContentLoaded', () => {
    // State
    let state = {
        authToken: sessionStorage.getItem('fdms_dashboard_session') || '',
        isAuthenticated: false,
        isDefaultPin: false,
        device: null,
        devices: [],
        selectedDeviceId: null,
        lastHealth: null,
        lastBranding: null,
        activeTab: 'overview',
        autoRefreshMs: 10000,
        refreshTimer: null,
        invoicesPage: 1,
        invoicesPageSize: 10,
        invoicesTotalPages: 1,
        filters: {
            search: '',
            status: '',
            currency: ''
        },
        queuePage: 1,
        queuePageSize: 10,
        queueTotalPages: 1,
        queueFilters: {
            search: '',
            status: '',
            deviceId: ''
        },
        zreportsPage: 1,
        zreportsPageSize: 10,
        zreportsTotalPages: 1,
        auditPage: 1,
        auditPageSize: 15,
        auditTotalPages: 1,
        auditInvoiceFilter: '',
        auditEventTypeFilter: '',
        auditFromDateFilter: '',
        auditToDateFilter: '',
        isSimulatedOffline: false,
        testLineItems: [
            { name: 'Standard Retail Product A', hsCode: '1001.99.00', qty: 2, price: 50.00, taxCode: 'A', taxPercent: 15 }
        ],
        taxCodes: [],
        isTaxCodesDirty: false,
        isScheduleDirty: false,
        lastTestResult: null,
        lastTestRequest: null,
        activeInspectorTab: 'receipt'
    };

    // Initialize Dashboard
    init();

    function init() {
        loadBranding();
        setupEventListeners();
        setupAuthEventListeners();
        setupTestFacilityEventListeners();
        setupReportsEventListeners();
        checkAuthStatus();
        loadAllData();
        fetchOperatingMode();
        setupAutoRefresh();
        loadPresetRetailUSD(); // Pre-fill test facility with realistic defaults
    }

    function isAuthModalOpen() {
        const pinModal = document.getElementById('modal-pin-login');
        const regModal = document.getElementById('modal-tenant-register');
        const forceModal = document.getElementById('modal-force-pin-change');
        const resetModal = document.getElementById('modal-reset-pin');

        return (pinModal && !pinModal.classList.contains('hidden')) ||
               (regModal && !regModal.classList.contains('hidden')) ||
               (forceModal && !forceModal.classList.contains('hidden')) ||
               (resetModal && !resetModal.classList.contains('hidden'));
    }

    // Central HTTP Request Helper with Session Authorization
    async function fetchWithAuth(url, options = {}) {
        options.headers = options.headers || {};

        if (state.authToken) {
            if (options.headers instanceof Headers) {
                options.headers.set('X-Dashboard-Session', state.authToken);
            } else {
                options.headers['X-Dashboard-Session'] = state.authToken;
            }
        }

        try {
            const res = await fetch(url, options);

            // Handle 401 Unauthorized from API middleware — only redirect to login for genuine auth failures
            // Exclude tenant endpoints (they manage their own session validation) and auth endpoints
            if (res.status === 401 && !url.includes('/auth/') && !url.includes('/api/v1/tenant/')) {
                if (!isAuthModalOpen()) {
                    showPinLoginModal();
                }
            }

            return res;
        } catch (err) {
            throw err;
        }
    }

    // Event Listeners Setup
    function setupEventListeners() {
        // Tab Navigation
        document.querySelectorAll('.nav-link').forEach(btn => {
            btn.addEventListener('click', (e) => {
                const targetTab = btn.getAttribute('data-tab');
                switchTab(targetTab);
            });
        });

        // Tab Jump Buttons
        document.querySelectorAll('[data-tab-jump]').forEach(btn => {
            btn.addEventListener('click', () => {
                const targetTab = btn.getAttribute('data-tab-jump');
                switchTab(targetTab);
            });
        });

        // Auto Refresh Selector
        const refreshSelect = document.getElementById('auto-refresh-select');
        if (refreshSelect) {
            refreshSelect.addEventListener('change', (e) => {
                state.autoRefreshMs = parseInt(e.target.value, 10);
                setupAutoRefresh();
            });
        }

        // Manual Refresh Button
        const btnManualRefresh = document.getElementById('btn-manual-refresh');
        if (btnManualRefresh) {
            btnManualRefresh.addEventListener('click', () => {
                loadAllData();
                fetchOperatingMode();
            });
        }

        // Target Environment Dropdown Select in Quick Actions
        const selectEnv = document.getElementById('select-target-env');
        if (selectEnv) {
            selectEnv.addEventListener('change', (e) => {
                setTargetEnvironment(e.target.value);
            });
        }

        // Quick Controls
        document.getElementById('btn-quick-open-day')?.addEventListener('click', handleOpenDay);
        document.getElementById('btn-quick-close-day')?.addEventListener('click', handleCloseDay);
        document.getElementById('btn-quick-sync-config')?.addEventListener('click', handleSyncConfig);
        document.getElementById('btn-quick-retry-queue')?.addEventListener('click', handleRetryQueue);
        document.getElementById('btn-quick-register-modal')?.addEventListener('click', () => switchTab('fiscal-day'));

        // Fiscal Day Tab Controls
        document.getElementById('btn-admin-open-day')?.addEventListener('click', handleOpenDay);
        document.getElementById('btn-admin-close-day')?.addEventListener('click', handleCloseDay);
        document.getElementById('btn-admin-sync')?.addEventListener('click', handleSyncConfig);
        document.getElementById('form-fiscal-day-schedule')?.addEventListener('submit', handleSaveSchedule);
        document.getElementById('btn-add-tax-code-row')?.addEventListener('click', addTaxCodeRow);
        document.getElementById('btn-save-tax-codes')?.addEventListener('click', handleSaveTaxCodes);

        document.querySelectorAll('#form-fiscal-day-schedule input').forEach(input => {
            input.addEventListener('change', markScheduleDirty);
            input.addEventListener('input', markScheduleDirty);
        });

        // Offline Queue Tab Retry
        document.getElementById('btn-retry-queue-tab')?.addEventListener('click', handleRetryQueue);

        // Active Device Select Change Listener
        document.getElementById('admin-device-select')?.addEventListener('change', (e) => {
            const selectedId = parseInt(e.target.value, 10);
            if (!isNaN(selectedId)) {
                state.selectedDeviceId = selectedId;
                const found = (state.devices || []).find(d => d.deviceId === selectedId);
                if (found) {
                    state.device = found;
                    renderFiscalDayAdmin(state.device, state.devices);
                    renderHeaderHealth(state.lastHealth, state.device);
                    renderMiniSidebarCard(state.device, state.lastHealth, state.lastBranding);
                }
            }
        });

        // Device Registration Form
        document.getElementById('form-register-device')?.addEventListener('submit', handleDeviceRegistration);

        // Invoice Filter Controls
        document.getElementById('btn-apply-filters')?.addEventListener('click', () => {
            state.filters.search = document.getElementById('filter-invoice-search').value.trim();
            state.filters.status = document.getElementById('filter-invoice-status').value;
            state.filters.currency = document.getElementById('filter-invoice-currency').value;
            state.invoicesPage = 1;
            loadInvoices();
        });

        // Pagination Controls
        document.getElementById('btn-page-prev')?.addEventListener('click', () => {
            if (state.invoicesPage > 1) {
                state.invoicesPage--;
                loadInvoices();
            }
        });

        document.getElementById('btn-page-next')?.addEventListener('click', () => {
            if (state.invoicesPage < state.invoicesTotalPages) {
                state.invoicesPage++;
                loadInvoices();
            }
        });

        // Offline Queue Filter Controls
        document.getElementById('btn-apply-queue-filters')?.addEventListener('click', () => {
            state.queueFilters.search = document.getElementById('filter-queue-search').value.trim();
            state.queueFilters.status = document.getElementById('filter-queue-status').value;
            state.queueFilters.deviceId = document.getElementById('filter-queue-device')?.value || '';
            state.queuePage = 1;
            loadQueueTable();
        });

        document.getElementById('filter-queue-device')?.addEventListener('change', (e) => {
            state.queueFilters.deviceId = e.target.value;
            state.queuePage = 1;
            loadQueueTable();
        });

        document.getElementById('filter-queue-search')?.addEventListener('keyup', (e) => {
            if (e.key === 'Enter') {
                state.queueFilters.search = e.target.value.trim();
                state.queuePage = 1;
                loadQueueTable();
            }
        });

        document.getElementById('filter-queue-status')?.addEventListener('change', (e) => {
            state.queueFilters.status = e.target.value;
            state.queuePage = 1;
            loadQueueTable();
        });

        // Offline Queue Pagination Controls
        document.getElementById('btn-queue-page-prev')?.addEventListener('click', () => {
            if (state.queuePage > 1) {
                state.queuePage--;
                loadQueueTable();
            }
        });

        document.getElementById('btn-queue-page-next')?.addEventListener('click', () => {
            if (state.queuePage < state.queueTotalPages) {
                state.queuePage++;
                loadQueueTable();
            }
        });

        // Audit Log Filter & Pagination Controls
        document.getElementById('btn-filter-audit')?.addEventListener('click', () => {
            state.auditInvoiceFilter = document.getElementById('filter-audit-invoice')?.value.trim() || '';
            state.auditEventTypeFilter = document.getElementById('filter-audit-type')?.value || '';
            state.auditFromDateFilter = document.getElementById('filter-audit-from-date')?.value || '';
            state.auditToDateFilter = document.getElementById('filter-audit-to-date')?.value || '';
            state.auditPage = 1;
            loadAuditLogs();
        });

        document.getElementById('filter-audit-invoice')?.addEventListener('keyup', (e) => {
            if (e.key === 'Enter') {
                state.auditInvoiceFilter = e.target.value.trim();
                state.auditPage = 1;
                loadAuditLogs();
            }
        });

        document.getElementById('filter-audit-type')?.addEventListener('change', () => {
            state.auditEventTypeFilter = document.getElementById('filter-audit-type').value;
            state.auditPage = 1;
            loadAuditLogs();
        });

        document.getElementById('filter-audit-from-date')?.addEventListener('change', () => {
            state.auditFromDateFilter = document.getElementById('filter-audit-from-date').value;
            state.auditPage = 1;
            loadAuditLogs();
        });

        document.getElementById('filter-audit-to-date')?.addEventListener('change', () => {
            state.auditToDateFilter = document.getElementById('filter-audit-to-date').value;
            state.auditPage = 1;
            loadAuditLogs();
        });

        document.getElementById('btn-reset-audit-filters')?.addEventListener('click', () => {
            const invInput = document.getElementById('filter-audit-invoice');
            const typeSelect = document.getElementById('filter-audit-type');
            const fromDateInput = document.getElementById('filter-audit-from-date');
            const toDateInput = document.getElementById('filter-audit-to-date');

            if (invInput) invInput.value = '';
            if (typeSelect) typeSelect.value = '';
            if (fromDateInput) fromDateInput.value = '';
            if (toDateInput) toDateInput.value = '';

            state.auditInvoiceFilter = '';
            state.auditEventTypeFilter = '';
            state.auditFromDateFilter = '';
            state.auditToDateFilter = '';
            state.auditPage = 1;
            loadAuditLogs();
        });

        document.getElementById('btn-audit-page-prev')?.addEventListener('click', () => {
            if (state.auditPage > 1) {
                state.auditPage--;
                loadAuditLogs();
            }
        });

        document.getElementById('btn-audit-page-next')?.addEventListener('click', () => {
            if (state.auditPage < state.auditTotalPages) {
                state.auditPage++;
                loadAuditLogs();
            }
        });

        // Modal Close Button
        document.getElementById('btn-close-invoice-modal')?.addEventListener('click', () => {
            document.getElementById('modal-invoice-details').classList.add('hidden');
        });

        // Close Modal on Overlay Click
        document.getElementById('modal-invoice-details')?.addEventListener('click', (e) => {
            if (e.target.id === 'modal-invoice-details') {
                document.getElementById('modal-invoice-details').classList.add('hidden');
            }
        });

        // No Registered Device Error Modal Controls
        document.getElementById('btn-close-no-device-modal')?.addEventListener('click', hideNoDeviceModal);
        document.getElementById('btn-dismiss-no-device-modal')?.addEventListener('click', hideNoDeviceModal);
        document.getElementById('modal-no-device-error')?.addEventListener('click', (e) => {
            if (e.target.id === 'modal-no-device-error') {
                hideNoDeviceModal();
            }
        });
        document.getElementById('btn-goto-device-reg')?.addEventListener('click', () => {
            hideNoDeviceModal();
            switchTab('fiscal-day');
            const regInput = document.getElementById('reg-device-id');
            if (regInput) {
                regInput.focus();
                regInput.scrollIntoView({ behavior: 'smooth', block: 'center' });
            }
        });

        // Subscription Tab Controls
        document.getElementById('form-activate-subscription')?.addEventListener('submit', handleActivateSubscription);
        document.getElementById('btn-generate-test-key')?.addEventListener('click', handleGenerateDevKey);
        document.getElementById('btn-copy-gen-key')?.addEventListener('click', copyGenKeyToClipboard);

        // Users & Permissions Modal Controls
        document.getElementById('btn-admin-open-add-user')?.addEventListener('click', () => {
            document.getElementById('modal-admin-add-user')?.classList.remove('hidden');
        });
        document.getElementById('btn-close-admin-add-user')?.addEventListener('click', () => {
            document.getElementById('modal-admin-add-user')?.classList.add('hidden');
        });
        document.getElementById('btn-cancel-admin-add-user')?.addEventListener('click', () => {
            document.getElementById('modal-admin-add-user')?.classList.add('hidden');
        });
        document.getElementById('form-admin-add-user')?.addEventListener('submit', handleCreateAdminUser);

        document.getElementById('btn-close-admin-edit-user')?.addEventListener('click', () => {
            document.getElementById('modal-admin-edit-user')?.classList.add('hidden');
        });
        document.getElementById('btn-cancel-admin-edit-user')?.addEventListener('click', () => {
            document.getElementById('modal-admin-edit-user')?.classList.add('hidden');
        });
        document.getElementById('form-admin-edit-user')?.addEventListener('submit', handleUpdateAdminUserPermissions);
    }

    // AUTHENTICATION & PIN MANAGEMENT HANDLERS
    function setupAuthEventListeners() {
        document.getElementById('form-pin-login')?.addEventListener('submit', handlePinLoginSubmit);
        document.getElementById('form-tenant-register')?.addEventListener('submit', handleTenantRegisterSubmit);
        document.getElementById('form-force-pin-change')?.addEventListener('submit', handleForcePinChangeSubmit);
        document.getElementById('form-reset-pin')?.addEventListener('submit', handleResetPinSubmit);

        document.getElementById('btn-open-register-modal')?.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            showRegisterModal();
        });
        document.getElementById('btn-back-to-tenant-login')?.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            showPinLoginModal();
        });
        document.getElementById('btn-open-forgot-pin')?.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            showResetPinModal();
        });
        document.getElementById('btn-back-to-login')?.addEventListener('click', (e) => {
            e.preventDefault();
            e.stopPropagation();
            showPinLoginModal();
        });
        document.getElementById('btn-lock-dashboard')?.addEventListener('click', handleLogout);

        window.showRegisterModal = showRegisterModal;
        window.showPinLoginModal = showPinLoginModal;
        window.showResetPinModal = showResetPinModal;
        window.showForcePinChangeModal = showForcePinChangeModal;
    }

    async function checkAuthStatus() {
        try {
            if (!state.authToken) {
                showPinLoginModal();
                return;
            }

            // Test current token with tenant me endpoint first
            const resTenant = await fetchWithAuth('/api/v1/tenant/auth/me');
            if (resTenant.ok) {
                state.isAuthenticated = true;
                hideAuthModals();
                updateAuthStatusUI(true);
                return;
            }

            // Fallback: check admin status
            const res = await fetchWithAuth('/api/v1/auth/status');
            if (!res.ok) {
                showPinLoginModal();
                return;
            }
            const data = await res.json();
            state.isAuthenticated = data.isAuthenticated;
            state.isDefaultPin = data.isDefaultPin;

            if (!data.isAuthenticated) {
                showPinLoginModal();
            } else if (data.isDefaultPin) {
                showForcePinChangeModal();
            } else {
                hideAuthModals();
                updateAuthStatusUI(true);
            }
        } catch (err) {
            console.error('Failed to check auth status:', err);
            showPinLoginModal();
        }
    }

    function showPinLoginModal(msg = '') {
        const modal = document.getElementById('modal-pin-login');
        const respBox = document.getElementById('login-pin-response');
        if (respBox) {
            if (msg) {
                respBox.classList.remove('hidden');
                respBox.className = 'response-box box-danger mb-3 p-2 text-center';
                respBox.textContent = msg;
            } else {
                respBox.classList.add('hidden');
            }
        }
        if (modal) modal.classList.remove('hidden');
        document.getElementById('modal-tenant-register')?.classList.add('hidden');
        document.getElementById('modal-force-pin-change')?.classList.add('hidden');
        document.getElementById('modal-reset-pin')?.classList.add('hidden');
        updateAuthStatusUI(false);

        const emailInput = document.getElementById('input-login-email');
        if (emailInput) {
            setTimeout(() => emailInput.focus(), 100);
        }
    }

    function showRegisterModal(msg = '') {
        const modal = document.getElementById('modal-tenant-register');
        const respBox = document.getElementById('register-response');
        if (respBox) {
            if (msg) {
                respBox.classList.remove('hidden');
                respBox.className = 'response-box box-danger mb-3 p-2 text-center';
                respBox.textContent = msg;
            } else {
                respBox.classList.add('hidden');
            }
        }
        if (modal) modal.classList.remove('hidden');
        document.getElementById('modal-pin-login')?.classList.add('hidden');
        document.getElementById('modal-force-pin-change')?.classList.add('hidden');
        document.getElementById('modal-reset-pin')?.classList.add('hidden');
        updateAuthStatusUI(false);

        const apiKeyInput = document.getElementById('input-register-apikey');
        if (apiKeyInput) {
            setTimeout(() => apiKeyInput.focus(), 100);
        }
    }

    function showForcePinChangeModal(msg = '') {
        const modal = document.getElementById('modal-force-pin-change');
        const respBox = document.getElementById('force-pin-response');
        if (respBox) {
            if (msg) {
                respBox.classList.remove('hidden');
                respBox.className = 'response-box box-danger mb-3 p-2 text-center';
                respBox.textContent = msg;
            } else {
                respBox.classList.add('hidden');
            }
        }
        if (modal) modal.classList.remove('hidden');
        document.getElementById('modal-pin-login')?.classList.add('hidden');
        document.getElementById('modal-tenant-register')?.classList.add('hidden');
        document.getElementById('modal-reset-pin')?.classList.add('hidden');
        updateAuthStatusUI(true);

        const newPinInput = document.getElementById('input-new-pin');
        if (newPinInput) {
            newPinInput.value = '';
            document.getElementById('input-confirm-pin').value = '';
            setTimeout(() => newPinInput.focus(), 100);
        }
    }

    function showResetPinModal(msg = '') {
        const modal = document.getElementById('modal-reset-pin');
        const respBox = document.getElementById('reset-pin-response');
        const isAlreadyVisible = modal && !modal.classList.contains('hidden');

        if (respBox) {
            if (msg) {
                respBox.classList.remove('hidden');
                respBox.className = 'response-box box-danger mb-3 p-2 text-center';
                respBox.textContent = msg;
            } else {
                respBox.classList.add('hidden');
            }
        }
        if (modal) modal.classList.remove('hidden');
        document.getElementById('modal-pin-login')?.classList.add('hidden');
        document.getElementById('modal-tenant-register')?.classList.add('hidden');
        document.getElementById('modal-force-pin-change')?.classList.add('hidden');

        const codeInput = document.getElementById('input-reset-code');
        if (codeInput && !isAlreadyVisible) {
            codeInput.value = '';
            setTimeout(() => codeInput.focus(), 100);
        }
    }

    function hideAuthModals() {
        document.getElementById('modal-pin-login')?.classList.add('hidden');
        document.getElementById('modal-tenant-register')?.classList.add('hidden');
        document.getElementById('modal-force-pin-change')?.classList.add('hidden');
        document.getElementById('modal-reset-pin')?.classList.add('hidden');
    }

    function updateAuthStatusUI(isAuth) {
        const badge = document.getElementById('auth-status-badge');
        const lockText = document.getElementById('btn-lock-text');

        if (badge) badge.classList.toggle('hidden', !isAuth);
        if (lockText) lockText.textContent = isAuth ? 'Lock' : 'Login';
    }

    async function handlePinLoginSubmit(e) {
        e.preventDefault();
        const emailInp = document.getElementById('input-login-email');
        const passInp = document.getElementById('input-login-password');
        const btn = document.getElementById('btn-submit-pin-login');
        const respBox = document.getElementById('login-pin-response');

        const email = emailInp ? emailInp.value.trim() : '';
        const password = passInp ? passInp.value.trim() : '';

        if (!email || !password) {
            showPinLoginModal('Please enter both Email/Username and Password.');
            return;
        }

        if (btn) btn.disabled = true;
        if (respBox) respBox.classList.add('hidden');

        try {
            // First attempt tenant user authentication
            const res = await fetch('/api/v1/tenant/auth/login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ email, password })
            });

            const data = await res.json();

            if (res.ok && data.success) {
                if (btn) btn.disabled = false;
                state.authToken = data.token;
                sessionStorage.setItem('fdms_dashboard_session', data.token);
                state.isAuthenticated = true;
                state.isDefaultPin = false;

                hideAuthModals();
                updateAuthStatusUI(true);
                loadAllData();
                return;
            }

            // Fallback attempt: check if password was entered as admin PIN
            const resAdmin = await fetch('/api/v1/auth/login', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ pin: password })
            });

            const dataAdmin = await resAdmin.json();
            if (btn) btn.disabled = false;

            if (resAdmin.ok && dataAdmin.success) {
                state.authToken = dataAdmin.token;
                sessionStorage.setItem('fdms_dashboard_session', dataAdmin.token);
                state.isAuthenticated = true;
                state.isDefaultPin = dataAdmin.isDefaultPin;

                if (dataAdmin.isDefaultPin) {
                    showForcePinChangeModal();
                } else {
                    hideAuthModals();
                    updateAuthStatusUI(true);
                    loadAllData();
                }
            } else {
                showPinLoginModal(data.message || 'Invalid email or password.');
            }
        } catch (err) {
            if (btn) btn.disabled = false;
            showPinLoginModal('Connection error. Failed to authenticate.');
        }
    }

    async function handleTenantRegisterSubmit(e) {
        e.preventDefault();
        const apiKeyInp = document.getElementById('input-register-apikey');
        const emailInp = document.getElementById('input-register-email');
        const nameInp = document.getElementById('input-register-fullname');
        const passInp = document.getElementById('input-register-password');
        const btn = document.getElementById('btn-submit-tenant-register');
        const respBox = document.getElementById('register-response');

        const tenantApiKey = apiKeyInp ? apiKeyInp.value.trim() : '';
        const email = emailInp ? emailInp.value.trim() : '';
        const fullName = nameInp ? nameInp.value.trim() : '';
        const password = passInp ? passInp.value.trim() : '';

        if (!tenantApiKey || !email || !password) {
            showRegisterModal('Tenant API Key, Email, and Password are required.');
            return;
        }

        if (btn) btn.disabled = true;
        if (respBox) respBox.classList.add('hidden');

        try {
            const res = await fetch('/api/v1/tenant/auth/register', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ tenantApiKey, email, fullName, password })
            });

            const data = await res.json();
            if (btn) btn.disabled = false;

            if (res.ok && data.success) {
                state.authToken = data.token;
                sessionStorage.setItem('fdms_dashboard_session', data.token);
                state.isAuthenticated = true;
                state.isDefaultPin = false;

                hideAuthModals();
                updateAuthStatusUI(true);
                alert(data.message || 'Registration successful! Welcome to the portal.');
                loadAllData();
            } else {
                showRegisterModal(data.message || 'Registration failed. Please check your Tenant API Key.');
            }
        } catch (err) {
            if (btn) btn.disabled = false;
            showRegisterModal('Connection error. Failed to complete registration.');
        }
    }

    async function handleForcePinChangeSubmit(e) {
        e.preventDefault();
        const newPin = document.getElementById('input-new-pin').value.trim();
        const confirmPin = document.getElementById('input-confirm-pin').value.trim();
        const btn = document.getElementById('btn-submit-force-pin');
        const respBox = document.getElementById('force-pin-response');

        if (!newPin || !confirmPin) {
            showForcePinChangeModal('Please enter and confirm your new PIN.');
            return;
        }

        if (newPin !== confirmPin) {
            showForcePinChangeModal('New PIN and Confirmation PIN do not match.');
            return;
        }

        if (newPin === '0000') {
            showForcePinChangeModal('Cannot reuse default PIN "0000". Please choose a new secure PIN.');
            return;
        }

        if (btn) btn.disabled = true;

        try {
            const res = await fetchWithAuth('/api/v1/auth/change-pin', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ currentPin: '0000', newPin: newPin })
            });

            const data = await res.json();
            if (btn) btn.disabled = false;

            if (res.ok) {
                if (data.token) {
                    state.authToken = data.token;
                    sessionStorage.setItem('fdms_dashboard_session', data.token);
                }
                state.isDefaultPin = false;
                hideAuthModals();
                updateAuthStatusUI(true);
                alert(data.message || 'PIN updated successfully. System unlocked.');
                loadAllData();
            } else {
                showForcePinChangeModal(data.message || 'Failed to update PIN.');
            }
        } catch (err) {
            if (btn) btn.disabled = false;
            showForcePinChangeModal('Error changing PIN: ' + err.message);
        }
    }

    async function handleResetPinSubmit(e) {
        e.preventDefault();
        const codeInp = document.getElementById('input-reset-code');
        const btn = document.getElementById('btn-submit-reset-pin');
        const respBox = document.getElementById('reset-pin-response');
        const resetCode = codeInp ? codeInp.value.trim() : '';

        if (!resetCode) return;

        if (btn) btn.disabled = true;
        if (respBox) respBox.classList.add('hidden');

        try {
            const res = await fetch('/api/v1/auth/reset-pin', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ resetCode })
            });

            const data = await res.json();
            if (btn) btn.disabled = false;

            if (res.ok) {
                state.authToken = '';
                sessionStorage.removeItem('fdms_dashboard_session');
                state.isAuthenticated = false;
                state.isDefaultPin = true;

                showPinLoginModal();
                alert(data.message || 'Admin PIN successfully reset to default "0000". Please enter "0000" to configure your new PIN.');
            } else {
                showResetPinModal(data.message || 'Invalid 16-digit master reset code.');
            }
        } catch (err) {
            if (btn) btn.disabled = false;
            showResetPinModal('Connection error. Failed to reset PIN.');
        }
    }

    async function handleLogout() {
        try {
            await fetchWithAuth('/api/v1/auth/logout', { method: 'POST' });
        } catch (err) {
            // Ignore error on logout
        } finally {
            state.authToken = '';
            state.isAuthenticated = false;
            sessionStorage.removeItem('fdms_dashboard_session');
            showPinLoginModal();
        }
    }

    // TEST FACILITY & SIMULATOR EVENT LISTENERS
    function setupTestFacilityEventListeners() {
        // Mode Switch Toggle
        const toggleSim = document.getElementById('toggle-sim-mode');
        if (toggleSim) {
            toggleSim.addEventListener('change', (e) => {
                toggleOperatingMode(e.target.checked);
            });
        }

        // Flush / Sync Queue Button
        document.getElementById('btn-sim-flush-queue')?.addEventListener('click', handleSimQueueFlush);

        // Auto Generate Invoice Number
        document.getElementById('btn-gen-invoice-no')?.addEventListener('click', generateInvoiceNumber);

        // Receipt Type Change Handler
        document.getElementById('test-receipt-type')?.addEventListener('change', (e) => {
            const refBox = document.getElementById('credit-debit-ref-box');
            if (refBox) {
                const isRef = e.target.value === 'CreditNote' || e.target.value === 'DebitNote';
                refBox.classList.toggle('hidden', !isRef);
            }
        });

        // Presets Loader Buttons
        document.getElementById('preset-retail-usd')?.addEventListener('click', (e) => { e.preventDefault(); loadPresetRetailUSD(); });
        document.getElementById('preset-split-multicurrency')?.addEventListener('click', (e) => { e.preventDefault(); loadPresetMultiCurrency(); });
        document.getElementById('preset-credit-note')?.addEventListener('click', (e) => { e.preventDefault(); loadPresetCreditNote(); });
        document.getElementById('preset-debit-note')?.addEventListener('click', (e) => { e.preventDefault(); loadPresetDebitNote(); });

        // Add Line Item Button
        document.getElementById('btn-add-line-item')?.addEventListener('click', () => {
            state.testLineItems.push({
                name: `Item ${state.testLineItems.length + 1}`,
                hsCode: '1001.99.00',
                qty: 1,
                price: 10.00,
                taxCode: 'A',
                taxPercent: 15
            });
            renderTestLineItems();
        });

        // Payment calc inputs change listeners
        document.querySelectorAll('.pay-calc-input').forEach(input => {
            input.addEventListener('input', calculateInvoiceTotals);
        });

        // Test Document Form Submit
        document.getElementById('form-test-invoice')?.addEventListener('submit', handleTestInvoiceSubmit);

        // Inspector Hangar Sub-Tabs
        document.querySelectorAll('[data-inspect-tab]').forEach(tabBtn => {
            tabBtn.addEventListener('click', () => {
                const tabKey = tabBtn.getAttribute('data-inspect-tab');
                switchInspectorTab(tabKey);
            });
        });
    }

    // Fetch Operating Mode Status from Server
    async function fetchOperatingMode() {
        try {
            const res = await fetchWithAuth('/api/v1/dashboard/mode');
            if (!res.ok) return;
            const data = await res.json();
            state.targetEnvironment = data.targetEnvironment || 'Sandbox';
            state.isSimulatedOffline = data.isSimulatedOffline;
            updateOperatingModeUI(data);
        } catch (err) {
            console.error('Failed to fetch operating mode:', err);
        }
    }

    // Set Target Environment (Live, Sandbox, Simulated)
    async function setTargetEnvironment(targetEnvironment) {
        try {
            const res = await fetchWithAuth('/api/v1/dashboard/mode', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ targetEnvironment })
            });

            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();
            state.targetEnvironment = data.targetEnvironment || targetEnvironment;
            state.isSimulatedOffline = data.isSimulatedOffline;
            updateOperatingModeUI(data);
            loadAllData();
        } catch (err) {
            alert('Failed to switch target environment: ' + err.message);
        }
    }

    // Toggle Operating Mode (Legacy switch support)
    async function toggleOperatingMode(isOffline) {
        await setTargetEnvironment(isOffline ? 'Simulated' : 'Sandbox');
    }

    // Update Mode UI Components
    function updateOperatingModeUI(data) {
        const targetEnv = (typeof data === 'object' ? data.targetEnvironment : null) || state.targetEnvironment || (data === true ? 'Simulated' : 'Sandbox');
        state.targetEnvironment = targetEnv;

        // 1. Update Target Environment Dropdown Select in Overview Quick Ribbon
        const selectEnv = document.getElementById('select-target-env');
        if (selectEnv) {
            selectEnv.value = targetEnv;
            selectEnv.setAttribute('data-env', targetEnv);
        }

        // 2. Update Top Header Mode Status Badge
        const badge = document.getElementById('mode-status-badge');
        const badgeText = document.getElementById('mode-status-text');
        const badgeIcon = badge?.querySelector('i');

        if (badge && badgeText) {
            if (targetEnv === 'Live') {
                badge.className = 'mode-status-badge mode-live';
                badgeText.textContent = 'ZIMRA LIVE';
                if (badgeIcon) badgeIcon.className = 'fa-solid fa-shield-halved';
            } else if (targetEnv === 'Simulated') {
                badge.className = 'mode-status-badge mode-simulated';
                badgeText.textContent = 'LOCAL SIMULATED';
                if (badgeIcon) badgeIcon.className = 'fa-solid fa-laptop-code';
            } else {
                badge.className = 'mode-status-badge mode-sandbox';
                badgeText.textContent = 'ZIMRA SANDBOX';
                if (badgeIcon) badgeIcon.className = 'fa-solid fa-flask';
            }
        }

        // 3. Update Simulator Toolbar
        const toggle = document.getElementById('toggle-sim-mode');
        const title = document.getElementById('sim-mode-title');
        const desc = document.getElementById('sim-mode-desc');
        const iconBox = document.getElementById('sim-mode-icon-box');

        if (toggle) toggle.checked = (targetEnv === 'Simulated');

        if (title && desc && iconBox) {
            if (targetEnv === 'Live') {
                title.textContent = 'Target Environment: ZIMRA LIVE';
                desc.textContent = 'Official Production mode: Invoices are submitted directly to ZIMRA Production server (https://fdmsapi.zimra.co.zw/).';
                iconBox.style.background = 'rgba(239, 68, 68, 0.2)';
                iconBox.style.borderColor = 'rgba(239, 68, 68, 0.4)';
                iconBox.style.color = '#EF4444';
                iconBox.innerHTML = '<i class="fa-solid fa-shield-halved"></i>';
            } else if (targetEnv === 'Simulated') {
                title.textContent = 'Target Environment: LOCAL SIMULATED SUCCESS';
                desc.textContent = 'Local Mock mode: Invoices instantly generate simulated ZIMRA signatures & QR codes locally without external calls.';
                iconBox.style.background = 'rgba(245, 158, 11, 0.2)';
                iconBox.style.borderColor = 'rgba(245, 158, 11, 0.4)';
                iconBox.style.color = '#F59E0B';
                iconBox.innerHTML = '<i class="fa-solid fa-laptop-code"></i>';
            } else {
                title.textContent = 'Target Environment: ZIMRA SANDBOX';
                desc.textContent = 'Test mode: Invoices are submitted directly to ZIMRA Sandbox test server (https://fdmsapitest.zimra.co.zw/).';
                iconBox.style.background = 'rgba(99, 102, 241, 0.2)';
                iconBox.style.borderColor = 'rgba(99, 102, 241, 0.3)';
                iconBox.style.color = '#818CF8';
                iconBox.innerHTML = '<i class="fa-solid fa-flask"></i>';
            }
        }
    }

    // Flush / Sync Queue Handler
    async function handleSimQueueFlush() {
        const btn = document.getElementById('btn-sim-flush-queue');
        btn.disabled = true;
        btn.innerHTML = `<i class="fa-solid fa-spinner fa-spin"></i> Syncing Queue...`;

        try {
            const res = await fetchWithAuth('/api/v1/dashboard/queue/retry', { method: 'POST' });
            const data = await res.json();
            btn.disabled = false;
            btn.innerHTML = `<i class="fa-solid fa-cloud-arrow-up"></i> Sync & Flush Queue`;

            alert(data.message || 'Queue sync initiated.');
            loadAllData();
        } catch (err) {
            btn.disabled = false;
            btn.innerHTML = `<i class="fa-solid fa-cloud-arrow-up"></i> Sync & Flush Queue`;
            alert('Failed to sync queue: ' + err.message);
        }
    }

    // Generate Invoice Number
    function generateInvoiceNumber() {
        const now = new Date();
        const yyyy = now.getFullYear();
        const mm = String(now.getMonth() + 1).padStart(2, '0');
        const dd = String(now.getDate()).padStart(2, '0');
        const rand = Math.floor(1000 + Math.random() * 9000);
        document.getElementById('test-invoice-no').value = `INV-${yyyy}${mm}${dd}-${rand}`;
    }

    // Preset 1: Standard Retail USD Invoice
    function loadPresetRetailUSD() {
        generateInvoiceNumber();
        document.getElementById('test-receipt-type').value = 'FiscalInvoice';
        document.getElementById('test-currency').value = 'USD';
        document.getElementById('credit-debit-ref-box').classList.add('hidden');

        document.getElementById('test-buyer-name').value = 'Harare Retail Trading Ltd';
        document.getElementById('test-buyer-tin').value = '2000987654';

        state.testLineItems = [
            { name: 'Standard Retail Product A', hsCode: '1001.99.00', qty: 2, price: 50.00, taxCode: 'A', taxPercent: 15 }
        ];

        document.getElementById('pay-cash-amt').value = '115.00';
        document.getElementById('pay-card-amt').value = '';
        document.getElementById('pay-mobile-amt').value = '';

        renderTestLineItems();
    }

    // Preset 2: Multi-Currency USD + ZiG Split
    function loadPresetMultiCurrency() {
        generateInvoiceNumber();
        document.getElementById('test-receipt-type').value = 'FiscalInvoice';
        document.getElementById('test-currency').value = 'USD';
        document.getElementById('credit-debit-ref-box').classList.add('hidden');

        document.getElementById('test-buyer-name').value = 'Commercial Distributors Inc';
        document.getElementById('test-buyer-tin').value = '2000555444';

        state.testLineItems = [
            { name: 'Industrial Electronics Pack', hsCode: '8504.40.90', qty: 1, price: 200.00, taxCode: 'A', taxPercent: 15 },
            { name: 'Heavy Duty Cables 10m', hsCode: '8544.49.00', qty: 2, price: 50.00, taxCode: 'A', taxPercent: 15 }
        ];

        document.getElementById('pay-cash-amt').value = '150.00';
        document.getElementById('pay-card-amt').value = '195.00';
        document.getElementById('pay-mobile-amt').value = '';

        renderTestLineItems();
    }

    // Preset 3: Credit Note
    function loadPresetCreditNote() {
        generateInvoiceNumber();
        document.getElementById('test-receipt-type').value = 'CreditNote';
        document.getElementById('test-currency').value = 'USD';

        const refBox = document.getElementById('credit-debit-ref-box');
        refBox.classList.remove('hidden');

        document.getElementById('test-ref-receipt-id').value = '10001';
        document.getElementById('test-ref-device-id').value = '1042';
        document.getElementById('test-ref-global-no').value = '1';
        document.getElementById('test-ref-day-no').value = '1';
        document.getElementById('test-ref-notes').value = 'Customer returned damaged goods item #A';

        document.getElementById('test-buyer-name').value = 'Harare Retail Trading Ltd';
        document.getElementById('test-buyer-tin').value = '2000987654';

        state.testLineItems = [
            { name: 'Returned Product A', hsCode: '1001.99.00', qty: 1, price: 50.00, taxCode: 'A', taxPercent: 15 }
        ];

        document.getElementById('pay-cash-amt').value = '57.50';
        document.getElementById('pay-card-amt').value = '';
        document.getElementById('pay-mobile-amt').value = '';

        renderTestLineItems();
    }

    // Preset 4: Debit Note
    function loadPresetDebitNote() {
        generateInvoiceNumber();
        document.getElementById('test-receipt-type').value = 'DebitNote';
        document.getElementById('test-currency').value = 'USD';

        const refBox = document.getElementById('credit-debit-ref-box');
        refBox.classList.remove('hidden');

        document.getElementById('test-ref-receipt-id').value = '10001';
        document.getElementById('test-ref-device-id').value = '1042';
        document.getElementById('test-ref-global-no').value = '1';
        document.getElementById('test-ref-day-no').value = '1';
        document.getElementById('test-ref-notes').value = 'Price adjustment surcharge per contract v2';

        document.getElementById('test-buyer-name').value = 'Commercial Distributors Inc';
        document.getElementById('test-buyer-tin').value = '2000555444';

        state.testLineItems = [
            { name: 'Price Adjustment Surcharge', hsCode: '8504.40.90', qty: 1, price: 25.00, taxCode: 'A', taxPercent: 15 }
        ];

        document.getElementById('pay-cash-amt').value = '28.75';
        document.getElementById('pay-card-amt').value = '';
        document.getElementById('pay-mobile-amt').value = '';

        renderTestLineItems();
    }

    // Render Test Line Items Table
    function renderTestLineItems() {
        const tbody = document.getElementById('test-line-items-body');
        if (!tbody) return;

        tbody.innerHTML = state.testLineItems.map((item, idx) => {
            const lineTotal = (item.qty * item.price) * (1 + item.taxPercent / 100);
            return `
                <tr>
                    <td>
                        <input type="text" class="form-input line-item-name" data-idx="${idx}" value="${escapeHtml(item.name)}">
                    </td>
                    <td>
                        <input type="text" class="form-input line-item-hscode" data-idx="${idx}" value="${escapeHtml(item.hsCode)}">
                    </td>
                    <td>
                        <input type="number" step="1" class="form-input line-item-qty" data-idx="${idx}" value="${item.qty}">
                    </td>
                    <td>
                        <input type="number" step="0.01" class="form-input line-item-price" data-idx="${idx}" value="${item.price.toFixed(2)}">
                    </td>
                    <td>
                        <select class="form-select line-item-tax" data-idx="${idx}">
                            <option value="15" ${item.taxPercent === 15 ? 'selected' : ''}>15% (A)</option>
                            <option value="0" ${item.taxPercent === 0 ? 'selected' : ''}>0% (B)</option>
                        </select>
                    </td>
                    <td><strong>$${lineTotal.toFixed(2)}</strong></td>
                    <td>
                        <button type="button" class="btn btn-icon btn-danger btn-remove-line" data-idx="${idx}">
                            <i class="fa-solid fa-trash-can"></i>
                        </button>
                    </td>
                </tr>
            `;
        }).join('');

        // Attach listeners for inline updates
        tbody.querySelectorAll('.line-item-name').forEach(inp => inp.addEventListener('input', (e) => {
            const idx = parseInt(e.target.getAttribute('data-idx'), 10);
            state.testLineItems[idx].name = e.target.value;
        }));

        tbody.querySelectorAll('.line-item-hscode').forEach(inp => inp.addEventListener('input', (e) => {
            const idx = parseInt(e.target.getAttribute('data-idx'), 10);
            state.testLineItems[idx].hsCode = e.target.value;
        }));

        tbody.querySelectorAll('.line-item-qty').forEach(inp => inp.addEventListener('input', (e) => {
            const idx = parseInt(e.target.getAttribute('data-idx'), 10);
            state.testLineItems[idx].qty = parseFloat(e.target.value) || 0;
            renderTestLineItems();
            calculateInvoiceTotals();
        }));

        tbody.querySelectorAll('.line-item-price').forEach(inp => inp.addEventListener('input', (e) => {
            const idx = parseInt(e.target.getAttribute('data-idx'), 10);
            state.testLineItems[idx].price = parseFloat(e.target.value) || 0;
            renderTestLineItems();
            calculateInvoiceTotals();
        }));

        tbody.querySelectorAll('.line-item-tax').forEach(sel => sel.addEventListener('change', (e) => {
            const idx = parseInt(e.target.getAttribute('data-idx'), 10);
            state.testLineItems[idx].taxPercent = parseFloat(e.target.value) || 0;
            renderTestLineItems();
            calculateInvoiceTotals();
        }));

        tbody.querySelectorAll('.btn-remove-line').forEach(btn => btn.addEventListener('click', (e) => {
            const idx = parseInt(btn.getAttribute('data-idx'), 10);
            if (state.testLineItems.length > 1) {
                state.testLineItems.splice(idx, 1);
                renderTestLineItems();
                calculateInvoiceTotals();
            } else {
                alert('An invoice must have at least 1 line item.');
            }
        }));

        calculateInvoiceTotals();
    }

    // Calculate Totals & Subtotals
    function calculateInvoiceTotals() {
        let subtotal = 0;
        let vatTotal = 0;

        state.testLineItems.forEach(item => {
            const net = (item.qty || 0) * (item.price || 0);
            const tax = net * ((item.taxPercent || 0) / 100);
            subtotal += net;
            vatTotal += tax;
        });

        const invoiceTotal = subtotal + vatTotal;
        const currency = document.getElementById('test-currency')?.value || 'USD';
        const symbol = currency === 'USD' ? '$' : 'ZiG ';

        document.getElementById('calc-subtotal-val').textContent = `${symbol}${subtotal.toFixed(2)}`;
        document.getElementById('calc-vat-val').textContent = `${symbol}${vatTotal.toFixed(2)}`;
        document.getElementById('calc-total-val').textContent = `${symbol}${invoiceTotal.toFixed(2)}`;
    }

    // Handle Test Invoice Submission
    async function handleTestInvoiceSubmit(e) {
        e.preventDefault();

        const btn = document.getElementById('btn-submit-test-invoice');
        btn.disabled = true;
        btn.innerHTML = `<i class="fa-solid fa-spinner fa-spin"></i> Processing & Hashing Receipt...`;

        const invoiceNo = document.getElementById('test-invoice-no').value.trim();
        const receiptType = document.getElementById('test-receipt-type').value;
        const currency = document.getElementById('test-currency').value;
        const buyerName = document.getElementById('test-buyer-name').value.trim();
        const buyerTin = document.getElementById('test-buyer-tin').value.trim();

        // Calculate line items & taxes
        let subtotal = 0;
        let vatAmount = 0;

        const lines = state.testLineItems.map((item, idx) => {
            const lineNet = item.qty * item.price;
            const lineTax = lineNet * (item.taxPercent / 100);
            const lineTotal = lineNet + lineTax;

            subtotal += lineNet;
            vatAmount += lineTax;

            return {
                receiptLineType: 'Sale',
                receiptLineNo: idx + 1,
                receiptLineHSCode: item.hsCode,
                receiptLineName: item.name,
                receiptLinePrice: item.price,
                receiptLineQuantity: item.qty,
                receiptLineTotal: lineTotal,
                taxCode: item.taxPercent > 0 ? 'A' : 'B',
                taxPercent: item.taxPercent,
                taxID: item.taxPercent > 0 ? 1 : 2
            };
        });

        const receiptTotal = subtotal + vatAmount;

        // Taxes summary array
        const taxes = [
            {
                taxCode: 'A',
                taxPercent: 15.00,
                taxID: 1,
                taxAmount: vatAmount,
                salesAmountWithTax: receiptTotal
            }
        ];

        // Payment tenders array
        const cashAmt = parseFloat(document.getElementById('pay-cash-amt').value) || 0;
        const cardAmt = parseFloat(document.getElementById('pay-card-amt').value) || 0;
        const mobileAmt = parseFloat(document.getElementById('pay-mobile-amt').value) || 0;

        const payments = [];
        if (cashAmt > 0) payments.push({ moneyTypeCode: 'Cash', paymentAmount: cashAmt });
        if (cardAmt > 0) payments.push({ moneyTypeCode: 'Card', paymentAmount: cardAmt });
        if (mobileAmt > 0) payments.push({ moneyTypeCode: 'EcoCash', paymentAmount: mobileAmt });

        if (payments.length === 0) {
            payments.push({ moneyTypeCode: 'Cash', paymentAmount: receiptTotal });
        }

        // Build Payload
        const payload = {
            invoiceNo: invoiceNo,
            currency: currency,
            receiptDate: new Date().toISOString(),
            receiptTotal: receiptTotal,
            receiptPrintForm: 'Receipt80mm',
            username: 'TestUser',
            userNameSurname: 'Test Operator',
            buyerData: (buyerName || buyerTin) ? {
                buyerRegisterName: buyerName || 'Walk-in Customer',
                buyerTIN: buyerTin || '1000000000',
                buyerTradeName: buyerName,
                vatNumber: '1000000000'
            } : null,
            lines: lines,
            taxes: taxes,
            payments: payments
        };

        // If Credit/Debit note, attach reference object
        if (receiptType === 'CreditNote' || receiptType === 'DebitNote') {
            payload.creditDebitNoteRef = {
                originalReceiptID: parseInt(document.getElementById('test-ref-receipt-id').value, 10) || 10001,
                originalDeviceID: parseInt(document.getElementById('test-ref-device-id').value, 10) || 1042,
                originalReceiptGlobalNo: parseInt(document.getElementById('test-ref-global-no').value, 10) || 1,
                originalFiscalDayNo: parseInt(document.getElementById('test-ref-day-no').value, 10) || 1
            };
            payload.receiptNotes = document.getElementById('test-ref-notes').value.trim() || 'Invoice adjustment';
        }

        state.lastTestRequest = payload;

        // Determine API URL based on receipt type
        let endpoint = '/api/v1/erp/invoices';
        if (receiptType === 'CreditNote') endpoint = '/api/v1/erp/credit-notes';
        if (receiptType === 'DebitNote') endpoint = '/api/v1/erp/debit-notes';

        try {
            const res = await fetchWithAuth(endpoint, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-API-Key': 'fdms-admin-key-secure-prod-placeholder'
                },
                body: JSON.stringify(payload)
            });

            const data = await res.json();
            btn.disabled = false;
            btn.innerHTML = `<i class="fa-solid fa-paper-plane"></i> Submit & Fiscalize Invoice`;

            state.lastTestResult = data;
            renderInspectorHangar(data, payload);
            loadAllData(); // Refresh tables & telemetry
            transitionToInspector();

        } catch (err) {
            btn.disabled = false;
            btn.innerHTML = `<i class="fa-solid fa-paper-plane"></i> Submit & Fiscalize Invoice`;
            alert('Error submitting test document: ' + err.message);
        }
    }

    // Smoothly transition / scroll to Fiscalization Inspector
    function transitionToInspector() {
        const inspectorPanel = document.getElementById('fiscalization-inspector-panel');
        if (inspectorPanel) {
            inspectorPanel.scrollIntoView({ behavior: 'smooth', block: 'start' });
            inspectorPanel.classList.remove('inspector-highlight');
            void inspectorPanel.offsetWidth; // Trigger reflow for animation restart
            inspectorPanel.classList.add('inspector-highlight');
        }
    }

    // Switch Inspector Tab
    function switchInspectorTab(tabKey) {
        state.activeInspectorTab = tabKey;
        document.querySelectorAll('[data-inspect-tab]').forEach(btn => {
            btn.classList.toggle('active', btn.getAttribute('data-inspect-tab') === tabKey);
        });

        document.querySelectorAll('.inspect-pane').forEach(pane => {
            pane.classList.toggle('active', pane.id === `pane-inspect-${tabKey}`);
        });

        if (state.lastTestResult) {
            renderInspectorHangar(state.lastTestResult, state.lastTestRequest);
        }
    }

    // Render Fiscalization Inspector Hangar Contents
    function renderInspectorHangar(result, request) {
        const badge = document.getElementById('sim-result-status-badge');
        const status = result.fiscalStatus || (result.isOfflineFiscalized ? 'Pending' : 'Fiscalized');

        if (badge) {
            badge.className = `badge ${getStatusBadgeClass(status)}`;
            badge.textContent = status === 'Pending' ? 'Pending (Offline Queued)' : status;
        }

        // 1. Thermal Receipt Render
        const receiptPlaceholder = document.getElementById('receipt-placeholder');
        const thermalPaper = document.getElementById('thermal-receipt-paper');

        if (receiptPlaceholder && thermalPaper) {
            receiptPlaceholder.classList.add('hidden');
            thermalPaper.classList.remove('hidden');

            const isOffline = result.isOfflineFiscalized || result.fiscalStatus === 'Pending';
            const qrImgSrc = result.qrCodeBase64Image
                ? `data:image/png;base64,${result.qrCodeBase64Image}`
                : '';

            thermalPaper.innerHTML = `
                <div class="receipt-header-center">
                    <h4>ZIMRA FISCAL RECEIPT</h4>
                    <p>REGISTERED FISCAL DEVICE</p>
                    <p>TAXPAYER: TAXPAYER 1042</p>
                    <p>TIN: 1000000000 | VAT: 1000000000</p>
                    <p>DEVICE ID: 1042 | SERIAL: SN1042</p>
                </div>

                <div class="receipt-line-row">
                    <span>INVOICE #:</span>
                    <strong>${escapeHtml(result.invoiceNo || request.invoiceNo)}</strong>
                </div>
                <div class="receipt-line-row">
                    <span>RECEIPT TYPE:</span>
                    <span>${escapeHtml(result.receiptType || 'FiscalInvoice')}</span>
                </div>
                <div class="receipt-line-row">
                    <span>DATE & TIME:</span>
                    <span>${formatDate(result.serverDate || new Date().toISOString())}</span>
                </div>
                <div class="receipt-line-row">
                    <span>FISCAL DAY #:</span>
                    <span>${result.fiscalDayNo || 0}</span>
                </div>
                <div class="receipt-line-row">
                    <span>GLOBAL RECEIPT #:</span>
                    <span>#${result.receiptGlobalNo || 0}</span>
                </div>

                <div class="receipt-divider"></div>

                ${(request.lines || []).map(l => `
                    <div class="receipt-line-row">
                        <span>${l.receiptLineQuantity}x ${escapeHtml(l.receiptLineName)}</span>
                        <span>${request.currency} ${(l.receiptLineTotal || 0).toFixed(2)}</span>
                    </div>
                `).join('')}

                <div class="receipt-divider"></div>

                <div class="receipt-line-row">
                    <span>TOTAL (${request.currency}):</span>
                    <strong>${request.currency} ${(result.totalAmount || request.receiptTotal || 0).toFixed(2)}</strong>
                </div>
                <div class="receipt-line-row">
                    <span>PAYMENT METHOD:</span>
                    <span>${(request.payments || []).map(p => p.moneyTypeCode).join(', ') || 'Cash'}</span>
                </div>

                <div class="receipt-divider"></div>

                <div style="font-size: 8px; word-break: break-all; margin-bottom: 4px;">
                    <strong>DEVICE HASH (SHA-256):</strong><br>${escapeHtml(result.deviceSignature?.hash || 'N/A')}
                </div>
                <div style="font-size: 8px; word-break: break-all; margin-bottom: 4px;">
                    <strong>ECDSA SIGNATURE:</strong><br>${escapeHtml(result.deviceSignature?.signature || 'N/A')}
                </div>

                <div class="receipt-qr-center">
                    ${qrImgSrc ? `<img src="${qrImgSrc}" alt="ZIMRA QR Code">` : '<div class="p-2 text-muted">QR Code pending sync</div>'}
                    <span>${escapeHtml(result.qrCodeUrl || '')}</span>
                </div>

                <div class="receipt-footer-text">
                    ${isOffline
                        ? '*** LOCAL OFFLINE FISCALIZED (QUEUED) ***'
                        : '*** OFFICIAL ZIMRA SERVER FISCALIZED ***'
                    }
                </div>
            `;
        }

        // 2. Crypto Audit Render
        const cryptoPlaceholder = document.getElementById('crypto-audit-placeholder');
        const cryptoContent = document.getElementById('crypto-audit-content');
        if (cryptoPlaceholder && cryptoContent) {
            cryptoPlaceholder.classList.add('hidden');
            cryptoContent.classList.remove('hidden');

            cryptoContent.innerHTML = `
                <div class="crypto-card">
                    <div class="crypto-title">Receipt Status & Operational State</div>
                    <span class="badge ${getStatusBadgeClass(status)}">${status}</span>
                    ${result.isOfflineFiscalized ? '<span class="badge badge-pending ml-2">Local ECDSA Signed</span>' : '<span class="badge badge-success ml-2">ZIMRA Server Verified</span>'}
                </div>

                <div class="crypto-card">
                    <div class="crypto-title">SHA-256 Receipt Device Hash (Section 13.2.1)</div>
                    <div class="crypto-val">${escapeHtml(result.deviceSignature?.hash || 'N/A')}</div>
                </div>

                <div class="crypto-card">
                    <div class="crypto-title">Previous Device Hash (Ledger Chain Link)</div>
                    <div class="crypto-val">${escapeHtml(result.previousDeviceHash || 'Genesis / Initial Hash')}</div>
                </div>

                <div class="crypto-card">
                    <div class="crypto-title">Local Device ECDSA secp256r1 Signature</div>
                    <div class="crypto-val">${escapeHtml(result.deviceSignature?.signature || 'N/A')}</div>
                </div>

                <div class="crypto-card">
                    <div class="crypto-title">ZIMRA FDMS Server Signature (FdmsSignature)</div>
                    <div class="crypto-val">${escapeHtml(result.serverSignature?.signature || (result.isOfflineFiscalized ? 'Pending online submission to ZIMRA FDMS' : 'N/A'))}</div>
                </div>

                <div class="crypto-card">
                    <div class="crypto-title">ZIMRA Operation ID & Receipt ID</div>
                    <div class="crypto-val">Receipt ID: ${result.receiptID || 'Unassigned (Offline)'} | Operation ID: ${escapeHtml(result.operationID || 'N/A')}</div>
                </div>
            `;
        }

        // 3. ESC/POS Stream Render
        const escposPlaceholder = document.getElementById('escpos-placeholder');
        const escposContent = document.getElementById('escpos-content');
        if (escposPlaceholder && escposContent) {
            escposPlaceholder.classList.add('hidden');
            escposContent.classList.remove('hidden');

            escposContent.innerHTML = `
                <div class="crypto-card">
                    <div class="crypto-title">80mm ESC/POS Binary Print Blob (Base64 Stream)</div>
                    <div class="code-box">${escapeHtml(result.escPosStreamBase64 || 'ESC/POS stream generated dynamically')}</div>
                </div>

                <div class="crypto-card mt-3">
                    <div class="crypto-title">ESC/POS Formatted Receipt Print Layout Text</div>
                    <pre class="code-box" style="white-space: pre-wrap; color: #6EE7B7;">${escapeHtml(result.printLayout?.headerText || '')}\n${escapeHtml(result.printLayout?.bodyText || '')}\n${escapeHtml(result.printLayout?.footerText || '')}</pre>
                </div>
            `;
        }

        // 4. Raw JSON Request & Response Render
        const jsonPlaceholder = document.getElementById('json-placeholder');
        const jsonContent = document.getElementById('json-content');
        if (jsonPlaceholder && jsonContent) {
            jsonPlaceholder.classList.add('hidden');
            jsonContent.classList.remove('hidden');

            jsonContent.innerHTML = `
                <div class="crypto-card">
                    <div class="crypto-title">API Response JSON Payload</div>
                    <pre class="code-box" style="white-space: pre-wrap; max-height: 250px;">${escapeHtml(JSON.stringify(result, null, 2))}</pre>
                </div>

                <div class="crypto-card mt-3">
                    <div class="crypto-title">API Ingestion Request JSON Payload</div>
                    <pre class="code-box" style="white-space: pre-wrap; max-height: 250px; color: #A78BFA;">${escapeHtml(JSON.stringify(request, null, 2))}</pre>
                </div>
            `;
        }
    }

    // Switch Tab
    function switchTab(tabId) {
        state.activeTab = tabId;

        // Update nav buttons
        document.querySelectorAll('.nav-link').forEach(btn => {
            btn.classList.toggle('active', btn.getAttribute('data-tab') === tabId);
        });

        // Update tab panes
        document.querySelectorAll('.tab-pane').forEach(pane => {
            pane.classList.toggle('active', pane.id === `tab-${tabId}`);
        });

        // Update page title
        const titleMap = {
            'overview': 'Overview',
            'fiscal-day': 'Administration',
            'offline-queue': 'Offline Queue',
            'invoices': 'Fiscal Invoices',
            'reports': 'Reports',
            'test-facility': 'Invoice Console',
            'subscription': 'Subscriptions',
            'audit-logs': 'System Audit',
            'users': 'Users & Permissions'
        };
        const titleElem = document.getElementById('page-title');
        if (titleElem) titleElem.textContent = titleMap[tabId] || 'FDMS Bridge Console';

        // Fetch data for specific tab
        if (tabId === 'invoices') loadInvoices();
        if (tabId === 'offline-queue') loadQueueTable();
        if (tabId === 'reports') {
            loadReportsConfig();
            loadZReportsTable();
        }
        if (tabId === 'audit-logs') loadAuditLogs();
        if (tabId === 'test-facility') fetchOperatingMode();
        if (tabId === 'subscription') loadSubscriptionDetails();
        if (tabId === 'users') loadUsersTab();
        if (tabId === 'fiscal-day') {
            loadFiscalDaySchedule();
            loadTaxCodes();
        }
    }

    // Setup Auto Refresh Interval
    function setupAutoRefresh() {
        if (state.refreshTimer) clearInterval(state.refreshTimer);

        if (state.autoRefreshMs > 0) {
            state.refreshTimer = setInterval(() => {
                if (!state.isAuthenticated || isAuthModalOpen()) return;
                loadAllData(true);
            }, state.autoRefreshMs);
        }
    }

    // Main Data Loading Pipeline
    async function loadAllData(isAutoRefresh = false) {
        if (!state.isAuthenticated || isAuthModalOpen()) return;
        await fetchSummaryData();
        await loadSubscriptionDetails();
        if (state.activeTab === 'invoices') await loadInvoices();
        if (state.activeTab === 'offline-queue') await loadQueueTable();
        if (state.activeTab === 'reports') {
            await loadReportsConfig();
            await loadZReportsTable();
        }
        if (state.activeTab === 'audit-logs') await loadAuditLogs();
        if (state.activeTab === 'users') await loadUsersTab();
        if (state.activeTab === 'fiscal-day') {
            if (!isAutoRefresh || !isScheduleEditing()) {
                await loadFiscalDaySchedule();
            }
            if (!isAutoRefresh || !isTaxCodesEditing()) {
                await loadTaxCodes();
            }
        }
    }

    // Fetch Summary API Endpoint
    async function fetchSummaryData() {
        try {
            const response = await fetchWithAuth('/api/v1/dashboard/summary');
            if (!response.ok) throw new Error(`HTTP ${response.status}`);
            const data = await response.json();

            state.devices = data.devices || [];
            state.lastHealth = data.health;
            state.lastBranding = data.branding;

            if (state.selectedDeviceId) {
                const found = state.devices.find(d => d.deviceId === state.selectedDeviceId);
                state.device = found || data.device || (state.devices.length > 0 ? state.devices[0] : null);
            } else {
                state.device = data.device || (state.devices.length > 0 ? state.devices[0] : null);
                if (state.device) {
                    state.selectedDeviceId = state.device.deviceId;
                }
            }

            renderHeaderHealth(data.health, state.device);
            renderOverviewKPIs(data.stats, data.queue, state.device, state.devices);
            renderMiniSidebarCard(state.device, data.health, data.branding);
            renderOverviewRecentTable(data.recentDocuments);
            renderOverviewAuditFeed(data.recentAuditLogs);
            renderFiscalDayAdmin(state.device, state.devices);
            renderQueueDeviceFilterOptions(state.devices);
            renderQueueStatus(data.queue);
        } catch (err) {
            console.error('Failed to fetch dashboard summary:', err);
            renderErrorHealth();
        }
    }

    // Load Offline Queue Table (Paginated)
    async function loadQueueTable() {
        const tbody = document.getElementById('queue-table-body');
        if (!tbody) return;

        tbody.innerHTML = `<tr><td colspan="6" class="text-center">Loading offline queue...</td></tr>`;

        try {
            const params = new URLSearchParams({
                page: state.queuePage,
                pageSize: state.queuePageSize
            });

            if (state.queueFilters.search) params.append('search', state.queueFilters.search);
            if (state.queueFilters.status) params.append('status', state.queueFilters.status);
            if (state.queueFilters.deviceId) params.append('deviceId', state.queueFilters.deviceId);

            const res = await fetchWithAuth(`/api/v1/dashboard/queue?${params.toString()}`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();

            state.queueTotalPages = data.totalPages || 1;
            updateQueuePaginationUI(data.totalItems, data.page, data.totalPages);
            renderQueueTable(data.items);

        } catch (err) {
            console.error('Error loading offline queue:', err);
            tbody.innerHTML = `<tr><td colspan="6" class="text-center text-danger">Failed to load offline queue: ${escapeHtml(err.message)}</td></tr>`;
        }
    }

    // Queue Pagination UI Update
    function updateQueuePaginationUI(totalItems, currentPage, totalPages) {
        const info = document.getElementById('queue-pagination-info');
        const text = document.getElementById('queue-page-current-text');
        const btnPrev = document.getElementById('btn-queue-page-prev');
        const btnNext = document.getElementById('btn-queue-page-next');

        if (info) info.textContent = `Showing page ${currentPage} of ${totalPages} (${totalItems} total queue items)`;
        if (text) text.textContent = `Page ${currentPage} of ${totalPages}`;

        if (btnPrev) btnPrev.disabled = currentPage <= 1;
        if (btnNext) btnNext.disabled = currentPage >= totalPages;
    }

    // Populate Offline Queue Device Filter Dropdown
    function renderQueueDeviceFilterOptions(devices) {
        const select = document.getElementById('filter-queue-device');
        if (!select) return;
        const currentVal = select.value;
        const devList = devices || [];
        select.innerHTML = `<option value="">All Devices</option>` + devList.map(d => `
            <option value="${d.deviceId}">Device #${d.deviceId} (${escapeHtml(d.taxPayerName || 'Device ' + d.deviceId)})</option>
        `).join('');
        if (currentVal) select.value = currentVal;
    }

    // Render Offline Queue Table
    function renderQueueTable(items) {
        const tbody = document.getElementById('queue-table-body');
        if (!tbody) return;

        if (!items || items.length === 0) {
            tbody.innerHTML = `<tr><td colspan="7" class="text-center">Offline queue is currently empty (No pending retries).</td></tr>`;
            return;
        }

        tbody.innerHTML = items.map(item => `
            <tr>
                <td><span class="badge badge-secondary" style="font-family: var(--font-mono); color: #38BDF8; background: rgba(56, 189, 248, 0.15); border: 1px solid rgba(56, 189, 248, 0.3);"><i class="fa-solid fa-microchip"></i> Dev #${item.deviceId}</span></td>
                <td><strong>${escapeHtml(item.invoiceNo)}</strong></td>
                <td><span class="badge badge-info">${escapeHtml(item.receiptType)}</span></td>
                <td><span class="badge badge-outline" style="font-family: var(--font-mono); font-weight: 600;">#${item.receiptGlobalNo || 0}</span></td>
                <td><span class="badge ${getStatusBadgeClass(item.status)}">${escapeHtml(item.status)}</span></td>
                <td>${formatDate(item.queuedAt)} (Retry #${item.retryCount})</td>
                <td>${escapeHtml(item.errorMessage || 'Queued for background dispatch')}</td>
            </tr>
        `).join('');
    }

    // Render Health Indicators
    function renderHeaderHealth(health, device) {
        const badge = document.getElementById('zimra-health-badge');
        const text = document.getElementById('zimra-health-text');
        const latency = document.getElementById('zimra-latency-text');
        const dayBadge = document.getElementById('fiscal-day-badge');
        const dayText = document.getElementById('fiscal-day-text');

        if (!badge || !text || !latency) return;

        badge.className = 'health-indicator';
        if (health && health.isHealthy) {
            badge.classList.add('healthy');
            text.textContent = 'ZIMRA API Online';
            latency.textContent = `${health.latencyMs || 0} ms`;
        } else {
            badge.classList.add('degraded');
            text.textContent = 'ZIMRA Offline / Queueing';
            latency.textContent = health?.lastError ? 'Error' : '-- ms';
        }

        if (dayBadge && dayText) {
            const status = device?.fiscalDayStatus || 'FiscalDayClosed';
            const isOpened = status === 'FiscalDayOpened';
            dayText.textContent = isOpened ? 'Open' : 'Closed';
            if (isOpened) {
                dayBadge.style.borderColor = 'rgba(16, 185, 129, 0.4)';
                dayBadge.style.color = '#6EE7B7';
            } else {
                dayBadge.style.borderColor = 'rgba(239, 68, 68, 0.4)';
                dayBadge.style.color = '#FCA5A5';
            }
        }
    }

    function renderErrorHealth() {
        const text = document.getElementById('zimra-health-text');
        if (text) text.textContent = 'Gateway Offline';
    }

    // Render Overview KPI Cards
    function renderOverviewKPIs(stats, queue, device, devices) {
        if (!stats) return;

        document.getElementById('kpi-sales-usd').textContent = `$${(stats.todayUsdSales || 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
        document.getElementById('kpi-sales-zig').textContent = `ZiG ${(stats.todayZigSales || 0).toLocaleString('en-US', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
        document.getElementById('kpi-total-fiscalized').textContent = (stats.totalFiscalized || 0).toLocaleString();
        document.getElementById('kpi-fiscalized-sub').textContent = `${stats.todayCount || 0} submitted today`;

        const queueDepth = queue?.totalPending ?? ((queue?.queued || 0) + (queue?.failed || 0) + (queue?.processing || 0));
        document.getElementById('kpi-queue-depth').textContent = queueDepth.toLocaleString();
        document.getElementById('kpi-queue-sub').textContent = `${queue?.processing || 0} processing | ${queue?.failed || 0} retrying / failed`;

        // Nav Badge
        const navQueueBadge = document.getElementById('nav-queue-badge');
        if (navQueueBadge) navQueueBadge.textContent = queueDepth;

        // Registered Devices KPI Card
        const devList = devices || [];
        const totalDevs = devList.length;
        const openDevs = devList.filter(d => d.fiscalDayStatus === 'FiscalDayOpened').length;
        const closedDevs = totalDevs - openDevs;

        const kpiRegDevs = document.getElementById('kpi-registered-devices');
        if (kpiRegDevs) {
            kpiRegDevs.textContent = `${totalDevs} Device${totalDevs !== 1 ? 's' : ''}`;
        }
        const kpiDevsSub = document.getElementById('kpi-devices-sub');
        if (kpiDevsSub) {
            kpiDevsSub.textContent = `${openDevs} Day Open | ${closedDevs} Day Closed`;
        }
    }

    // Render Sidebar Device Card
    function renderMiniSidebarCard(device, health, branding) {
        const dot = document.getElementById('mini-status-dot');
        const name = document.getElementById('mini-client-name') || document.getElementById('mini-device-name');
        const tin = document.getElementById('mini-tin');

        if (dot) {
            dot.className = 'device-mini-status pulse-dot ' + (health?.isHealthy ? 'status-online' : 'status-degraded');
        }

        const taxPayerName = (device?.taxPayerName && device.taxPayerName.trim()) ? device.taxPayerName.trim() : 'Config pending';
        const taxPayerTIN = (device?.taxPayerTIN && device.taxPayerTIN.trim()) ? device.taxPayerTIN.trim() : 'Config pending';

        if (name) {
            name.textContent = `Client: ${taxPayerName}`;
            name.title = taxPayerName;
        }
        if (tin) {
            tin.textContent = `TIN: ${taxPayerTIN}`;
        }
    }

    // Render Recent Invoices Table
    function renderOverviewRecentTable(documents) {
        const tbody = document.getElementById('overview-recent-invoices');
        if (!tbody) return;

        if (!documents || documents.length === 0) {
            tbody.innerHTML = `<tr><td colspan="6" class="text-center">No fiscalized documents recorded yet.</td></tr>`;
            return;
        }

        tbody.innerHTML = documents.map(doc => `
            <tr>
                <td><strong>${escapeHtml(doc.invoiceNo)}</strong></td>
                <td><span class="badge badge-info">${escapeHtml(doc.receiptType)}</span></td>
                <td>${escapeHtml(doc.currency)} ${(doc.totalAmount || 0).toFixed(2)}</td>
                <td><span class="badge ${getStatusBadgeClass(doc.fiscalStatus)}">${escapeHtml(doc.fiscalStatus)}</span></td>
                <td>${formatTime(doc.createdAt)}</td>
                <td>
                    <button class="btn btn-icon btn-view-invoice" data-invoice="${escapeHtml(doc.invoiceNo)}">
                        <i class="fa-solid fa-eye"></i>
                    </button>
                </td>
            </tr>
        `).join('');

        // Attach modal trigger listeners
        tbody.querySelectorAll('.btn-view-invoice').forEach(btn => {
            btn.addEventListener('click', () => {
                const invNo = btn.getAttribute('data-invoice');
                showInvoiceModal(invNo);
            });
        });
    }

    // Render Overview Live Audit Feed
    function renderOverviewAuditFeed(logs) {
        const container = document.getElementById('overview-audit-feed');
        if (!container) return;

        if (!logs || logs.length === 0) {
            container.innerHTML = `<div class="text-center p-3">No system audit logs found.</div>`;
            return;
        }

        container.innerHTML = logs.map(log => `
            <div class="audit-item">
                <div class="audit-item-header">
                    <span class="audit-item-type">[${escapeHtml(log.eventType || 'SYSTEM')}]</span>
                    <span class="audit-item-time">${formatTime(log.timestamp)}</span>
                </div>
                <div class="audit-item-details">
                    ${log.invoiceNo ? `Invoice <strong>${escapeHtml(log.invoiceNo)}</strong>: ` : ''}
                    ${escapeHtml(log.details || log.errorMessage || 'Operation recorded')}
                </div>
            </div>
        `).join('');
    }

    // Render Fiscal Day Admin Tab
    function renderFiscalDayAdmin(device, devices) {
        // Populate DeviceId Dropdown
        const devSelect = document.getElementById('admin-device-select');
        const devList = devices || [];

        if (devSelect) {
            if (devList.length === 0) {
                devSelect.innerHTML = `<option value="">No Registered Devices</option>`;
            } else {
                devSelect.innerHTML = devList.map(d => `
                    <option value="${d.deviceId}" ${device && d.deviceId === device.deviceId ? 'selected' : ''}>
                        Device #${d.deviceId} (${escapeHtml(d.taxPayerName || 'Device ' + d.deviceId)})
                    </option>
                `).join('');
            }
        }

        if (!device) return;

        document.getElementById('admin-serial').textContent = device.deviceSerialNo || '--';
        document.getElementById('admin-taxpayer-name').textContent = (device.taxPayerName && device.taxPayerName.trim()) ? device.taxPayerName.trim() : 'Config pending';
        document.getElementById('admin-tin').textContent = (device.taxPayerTIN && device.taxPayerTIN.trim()) ? device.taxPayerTIN.trim() : 'Config pending';
        document.getElementById('admin-fiscal-day-no').textContent = `#${device.currentFiscalDayNo || 0}`;

        const statusElem = document.getElementById('admin-fiscal-day-status');
        if (statusElem) {
            statusElem.textContent = device.fiscalDayStatus || 'FiscalDayClosed';
            statusElem.className = `detail-value badge ${device.fiscalDayStatus === 'FiscalDayOpened' ? 'badge-success' : 'badge-pending'}`;
        }

        document.getElementById('admin-day-opened-at').textContent = device.fiscalDayOpenedAt ? formatDate(device.fiscalDayOpenedAt) : 'Not Opened';
        document.getElementById('admin-global-counter').textContent = `#${device.currentReceiptGlobalNo || 0}`;
        document.getElementById('admin-device-mode').textContent = `Operating Mode: ${device.deviceOperatingMode || 'Online'}`;

        const certElem = document.getElementById('admin-cert-status');
        if (certElem) {
            certElem.innerHTML = device.hasCertificate
                ? `<i class="fa-solid fa-circle-check text-success"></i> Valid ECC secp256r1 Cert`
                : `<i class="fa-solid fa-circle-xmark text-danger"></i> Missing / Local Placeholder`;
        }
    }

    // Dirty State Helpers
    function isTaxCodesEditing() {
        if (state.isTaxCodesDirty) return true;
        const tbody = document.getElementById('tax-codes-table-body');
        if (tbody && tbody.contains(document.activeElement)) return true;
        return false;
    }

    function isScheduleEditing() {
        if (state.isScheduleDirty) return true;
        const form = document.getElementById('form-fiscal-day-schedule');
        if (form && form.contains(document.activeElement)) return true;
        return false;
    }

    function markTaxCodesDirty() {
        state.isTaxCodesDirty = true;
        updateTaxCodesUnsavedBadge();
    }

    function updateTaxCodesUnsavedBadge() {
        const badge = document.getElementById('tax-codes-unsaved-badge');
        if (badge) {
            badge.classList.toggle('hidden', !state.isTaxCodesDirty);
        }
    }

    function markScheduleDirty() {
        state.isScheduleDirty = true;
        updateScheduleUnsavedBadge();
    }

    function updateScheduleUnsavedBadge() {
        const badge = document.getElementById('sched-unsaved-badge');
        if (badge) {
            badge.classList.toggle('hidden', !state.isScheduleDirty);
        }
    }

    // Load Fiscal Day Schedule
    async function loadFiscalDaySchedule(force = false) {
        if (!force && isScheduleEditing()) return;

        try {
            const res = await fetchWithAuth('/api/v1/admin/schedule');
            if (!res.ok) return;
            const sched = await res.json();

            const enabledElem = document.getElementById('sched-enabled');
            const openTimeElem = document.getElementById('sched-open-time');
            const closeTimeElem = document.getElementById('sched-close-time');
            const maxHrsElem = document.getElementById('sched-max-hrs');
            const badgeElem = document.getElementById('sched-status-badge');

            if (enabledElem) enabledElem.checked = !!sched.enabled;
            if (openTimeElem) openTimeElem.value = sched.autoOpenTimeCAT || '06:00';
            if (closeTimeElem) closeTimeElem.value = sched.autoCloseTimeCAT || '22:00';
            if (maxHrsElem) maxHrsElem.value = sched.taxPayerDayMaxHrs || 24;

            if (badgeElem) {
                badgeElem.className = `badge ${sched.enabled ? 'badge-success' : 'badge-secondary'}`;
                badgeElem.textContent = sched.enabled ? 'Schedule: Active' : 'Schedule: Off';
            }

            state.isScheduleDirty = false;
            updateScheduleUnsavedBadge();
        } catch (err) {
            console.error('Failed to load fiscal day schedule:', err);
        }
    }

    // Handle Save Schedule Form
    async function handleSaveSchedule(e) {
        if (e) e.preventDefault();
        const btn = document.getElementById('btn-save-schedule');
        const box = document.getElementById('sched-response-box');

        if (btn) btn.disabled = true;

        const payload = {
            enabled: document.getElementById('sched-enabled').checked,
            autoOpenTimeCAT: document.getElementById('sched-open-time').value,
            autoCloseTimeCAT: document.getElementById('sched-close-time').value,
            taxPayerDayMaxHrs: parseInt(document.getElementById('sched-max-hrs').value, 10) || 24,
            timeZone: document.getElementById('sched-timezone').value || '(UTC + 02:00) Harare, Pretoria',
            dayEndNotificationHrs: 2
        };

        try {
            const res = await fetchWithAuth('/api/v1/admin/schedule', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-API-Key': 'fdms-admin-key-secure-prod-placeholder'
                },
                body: JSON.stringify(payload)
            });

            const data = await res.json();
            if (btn) btn.disabled = false;

            if (res.ok) {
                state.isScheduleDirty = false;
                updateScheduleUnsavedBadge();
                if (box) {
                    box.classList.remove('hidden');
                    box.className = 'response-box p-3 glass mt-3 text-success';
                    box.innerHTML = `<strong>Success!</strong> ${escapeHtml(data.message || 'Schedule saved successfully.')}`;
                }
                loadFiscalDaySchedule(true);
            } else {
                alert('Failed to save schedule: ' + (data.error || data.message || 'Unknown error'));
            }
        } catch (err) {
            if (btn) btn.disabled = false;
            alert('Error saving schedule: ' + err.message);
        }
    }

    // Load Tax Code Mappings
    async function loadTaxCodes(force = false) {
        const tbody = document.getElementById('tax-codes-table-body');
        if (!tbody) return;

        if (!force && isTaxCodesEditing()) {
            return; // Preserve unsaved user edits during auto-refresh
        }

        try {
            const res = await fetchWithAuth('/api/v1/admin/tax-codes');
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();

            if (!Array.isArray(data)) {
                throw new Error(data.error || data.message || 'Invalid tax code data format');
            }

            state.taxCodes = data.map(tc => ({
                erpTaxCode: tc.erpTaxCode || '',
                zimraTaxCode: tc.zimraTaxCode || 'A',
                taxID: tc.taxID || 1,
                taxPercent: tc.taxPercent !== null && tc.taxPercent !== undefined ? tc.taxPercent : null,
                description: tc.description || ''
            }));

            state.isTaxCodesDirty = false;
            renderTaxCodesTable();
        } catch (err) {
            console.error('Failed to load tax codes:', err);
            tbody.innerHTML = `
                <tr>
                    <td colspan="6" class="text-center text-danger p-2" style="font-size: 12px;">
                        <i class="fa-solid fa-triangle-exclamation mr-1"></i> Failed to load tax codes: ${escapeHtml(err.message)}
                        <br>
                        <button type="button" class="btn btn-secondary btn-sm mt-1" id="btn-retry-tax-codes" style="padding: 2px 8px; font-size: 11px;">
                            <i class="fa-solid fa-rotate-right"></i> Retry Loading
                        </button>
                    </td>
                </tr>`;
            document.getElementById('btn-retry-tax-codes')?.addEventListener('click', () => loadTaxCodes(true));
        }
    }

    // Render Tax Codes Table
    function renderTaxCodesTable() {
        const tbody = document.getElementById('tax-codes-table-body');
        if (!tbody) return;

        updateTaxCodesUnsavedBadge();

        if (!state.taxCodes || state.taxCodes.length === 0) {
            tbody.innerHTML = `<tr><td colspan="6" class="text-center">No tax code mappings configured. Click "Add Tax Code" to create one.</td></tr>`;
            return;
        }

        tbody.innerHTML = state.taxCodes.map((tc, idx) => `
            <tr>
                <td>
                    <input type="text" class="form-input tax-erp-code" data-idx="${idx}" value="${escapeHtml(tc.erpTaxCode || '')}" placeholder="15%_VAT">
                </td>
                <td>
                    <input type="text" class="form-input tax-zimra-code" data-idx="${idx}" value="${escapeHtml(tc.zimraTaxCode || 'A')}" placeholder="A">
                </td>
                <td>
                    <input type="number" class="form-input tax-id" data-idx="${idx}" value="${tc.taxID || 1}" min="1">
                </td>
                <td>
                    <input type="number" step="0.01" class="form-input tax-percent" data-idx="${idx}" value="${tc.taxPercent !== null && tc.taxPercent !== undefined ? tc.taxPercent : ''}" placeholder="Exempt">
                </td>
                <td>
                    <input type="text" class="form-input tax-desc" data-idx="${idx}" value="${escapeHtml(tc.description || '')}" placeholder="Description">
                </td>
                <td class="text-center">
                    <button type="button" class="btn btn-icon btn-danger btn-remove-tax-code" data-idx="${idx}" title="Remove Tax Code" style="padding: 4px 6px; font-size: 11px;">
                        <i class="fa-solid fa-trash-can"></i>
                    </button>
                </td>
            </tr>
        `).join('');

        tbody.querySelectorAll('.tax-erp-code').forEach(inp => inp.addEventListener('input', (e) => {
            const idx = parseInt(e.target.getAttribute('data-idx'), 10);
            state.taxCodes[idx].erpTaxCode = e.target.value.trim();
            markTaxCodesDirty();
        }));

        tbody.querySelectorAll('.tax-zimra-code').forEach(inp => inp.addEventListener('input', (e) => {
            const idx = parseInt(e.target.getAttribute('data-idx'), 10);
            state.taxCodes[idx].zimraTaxCode = e.target.value.trim();
            markTaxCodesDirty();
        }));

        tbody.querySelectorAll('.tax-id').forEach(inp => inp.addEventListener('input', (e) => {
            const idx = parseInt(e.target.getAttribute('data-idx'), 10);
            state.taxCodes[idx].taxID = parseInt(e.target.value, 10) || 1;
            markTaxCodesDirty();
        }));

        tbody.querySelectorAll('.tax-percent').forEach(inp => inp.addEventListener('input', (e) => {
            const idx = parseInt(e.target.getAttribute('data-idx'), 10);
            const val = e.target.value.trim();
            state.taxCodes[idx].taxPercent = val === '' ? null : parseFloat(val);
            markTaxCodesDirty();
        }));

        tbody.querySelectorAll('.tax-desc').forEach(inp => inp.addEventListener('input', (e) => {
            const idx = parseInt(e.target.getAttribute('data-idx'), 10);
            state.taxCodes[idx].description = e.target.value;
            markTaxCodesDirty();
        }));

        tbody.querySelectorAll('.btn-remove-tax-code').forEach(btn => btn.addEventListener('click', (e) => {
            const idx = parseInt(btn.getAttribute('data-idx'), 10);
            state.taxCodes.splice(idx, 1);
            markTaxCodesDirty();
            renderTaxCodesTable();
        }));
    }

    // Add Row for Tax Code
    function addTaxCodeRow() {
        if (!state.taxCodes) state.taxCodes = [];
        state.taxCodes.push({
            erpTaxCode: `NEW_TAX_${state.taxCodes.length + 1}`,
            zimraTaxCode: 'A',
            taxID: state.taxCodes.length + 1,
            taxPercent: 15.00,
            description: 'Custom Tax Code'
        });
        markTaxCodesDirty();
        renderTaxCodesTable();
    }

    // Handle Save Tax Codes
    async function handleSaveTaxCodes() {
        const btn = document.getElementById('btn-save-tax-codes');
        const box = document.getElementById('tax-codes-response-box');
        if (btn) btn.disabled = true;

        try {
            const res = await fetchWithAuth('/api/v1/admin/tax-codes', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-API-Key': 'fdms-admin-key-secure-prod-placeholder'
                },
                body: JSON.stringify(state.taxCodes)
            });

            const data = await res.json();
            if (btn) btn.disabled = false;

            if (res.ok) {
                state.isTaxCodesDirty = false;
                updateTaxCodesUnsavedBadge();
                if (box) {
                    box.classList.remove('hidden');
                    box.className = 'response-box p-3 glass mt-3 text-success';
                    box.innerHTML = `<strong>Success!</strong> ${escapeHtml(data.message || 'Tax code mappings saved successfully.')}`;
                }
                loadTaxCodes(true);
            } else {
                alert('Failed to save tax codes: ' + (data.error || data.message || 'Unknown error'));
            }
        } catch (err) {
            if (btn) btn.disabled = false;
            alert('Error saving tax codes: ' + err.message);
        }
    }

    // Render Queue Status
    function renderQueueStatus(queue) {
        if (!queue) return;

        document.getElementById('q-count-queued').textContent = queue.queued || 0;
        document.getElementById('q-count-processing').textContent = queue.processing || 0;
        document.getElementById('q-count-failed').textContent = queue.failed || 0;
        document.getElementById('q-count-completed').textContent = queue.completed || 0;
    }

    // Load Invoices Tab
    async function loadInvoices() {
        const tbody = document.getElementById('invoices-table-body');
        if (!tbody) return;

        tbody.innerHTML = `<tr><td colspan="8" class="text-center">Loading fiscal documents...</td></tr>`;

        try {
            const params = new URLSearchParams({
                page: state.invoicesPage,
                pageSize: state.invoicesPageSize
            });

            if (state.filters.search) params.append('search', state.filters.search);
            if (state.filters.status) params.append('status', state.filters.status);
            if (state.filters.currency) params.append('currency', state.filters.currency);

            const res = await fetchWithAuth(`/api/v1/dashboard/documents?${params.toString()}`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();

            state.invoicesTotalPages = data.totalPages || 1;
            updatePaginationUI(data.totalItems, data.page, data.totalPages);

            if (!data.items || data.items.length === 0) {
                tbody.innerHTML = `<tr><td colspan="8" class="text-center">No matching fiscal documents found.</td></tr>`;
                return;
            }

            tbody.innerHTML = data.items.map(item => `
                <tr>
                    <td><strong>${escapeHtml(item.invoiceNo)}</strong></td>
                    <td><span class="badge badge-info">${escapeHtml(item.receiptType)}</span></td>
                    <td>${escapeHtml(item.currency)}</td>
                    <td>${(item.totalAmount || 0).toFixed(2)}</td>
                    <td>#${item.receiptGlobalNo || 0}</td>
                    <td><span class="badge ${getStatusBadgeClass(item.fiscalStatus)}">${escapeHtml(item.fiscalStatus)}</span></td>
                    <td>${formatDate(item.createdAt)}</td>
                    <td>
                        <button class="btn btn-secondary btn-sm btn-view-invoice" data-invoice="${escapeHtml(item.invoiceNo)}">
                            <i class="fa-solid fa-qrcode"></i> Details & QR
                        </button>
                    </td>
                </tr>
            `).join('');

            tbody.querySelectorAll('.btn-view-invoice').forEach(btn => {
                btn.addEventListener('click', () => {
                    const invNo = btn.getAttribute('data-invoice');
                    showInvoiceModal(invNo);
                });
            });

        } catch (err) {
            console.error('Error loading invoices:', err);
            tbody.innerHTML = `<tr><td colspan="8" class="text-center text-danger">Failed to load invoices.</td></tr>`;
        }
    }

    // Pagination UI Update
    function updatePaginationUI(totalItems, currentPage, totalPages) {
        const info = document.getElementById('pagination-info');
        const text = document.getElementById('page-current-text');
        const btnPrev = document.getElementById('btn-page-prev');
        const btnNext = document.getElementById('btn-page-next');

        if (info) info.textContent = `Showing page ${currentPage} of ${totalPages} (${totalItems} total documents)`;
        if (text) text.textContent = `Page ${currentPage} of ${totalPages}`;

        if (btnPrev) btnPrev.disabled = currentPage <= 1;
        if (btnNext) btnNext.disabled = currentPage >= totalPages;
    }

    // Show Invoice & QR Modal
    async function showInvoiceModal(invoiceNo) {
        const modal = document.getElementById('modal-invoice-details');
        const body = document.getElementById('modal-invoice-body');
        if (!modal || !body) return;

        body.innerHTML = `<div class="text-center p-3"><i class="fa-solid fa-spinner fa-spin"></i> Fetching receipt details from bridge...</div>`;
        modal.classList.remove('hidden');

        try {
            const res = await fetchWithAuth(`/api/v1/erp/documents/${encodeURIComponent(invoiceNo)}`, {
                headers: { 'X-API-Key': 'fdms-admin-key-secure-prod-placeholder' }
            });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const doc = await res.json();

            body.innerHTML = `
                <div class="qr-container">
                    ${doc.qrCodeBase64Image
                        ? `<img src="data:image/png;base64,${doc.qrCodeBase64Image}" alt="ZIMRA QR Code">`
                        : `<div class="text-center text-muted"><i class="fa-solid fa-qrcode fa-3x"></i><br>QR Code pending ZIMRA submission</div>`
                    }
                    <span class="badge ${getStatusBadgeClass(doc.fiscalStatus)}">${escapeHtml(doc.fiscalStatus)}</span>
                    <span class="text-muted" style="font-size: 11px;">Receipt ID: ${doc.receiptID || 'Unsubmitted'}</span>
                </div>

                <div class="device-details-grid">
                    <div class="detail-item">
                        <span class="detail-label">Invoice Number</span>
                        <span class="detail-value highlight">${escapeHtml(doc.invoiceNo)}</span>
                    </div>
                    <div class="detail-item">
                        <span class="detail-label">Global Receipt #</span>
                        <span class="detail-value">#${doc.receiptGlobalNo || 0}</span>
                    </div>
                    <div class="detail-item">
                        <span class="detail-label">Fiscal Day No</span>
                        <span class="detail-value">Day #${doc.fiscalDayNo || 0}</span>
                    </div>
                    <div class="detail-item">
                        <span class="detail-label">Operation ID</span>
                        <span class="detail-value">${escapeHtml(doc.operationID || 'N/A')}</span>
                    </div>
                </div>

                <div class="mt-3">
                    <label class="form-group label"><strong>Device Receipt Hash (SHA-256)</strong></label>
                    <div class="code-box">${escapeHtml(doc.deviceSignature?.hash || 'N/A')}</div>
                </div>

                <div class="mt-3">
                    <label class="form-group label"><strong>ECDSA Device Signature</strong></label>
                    <div class="code-box">${escapeHtml(doc.deviceSignature?.signature || 'N/A')}</div>
                </div>

                ${doc.qrCodeUrl ? `
                    <div class="mt-3">
                        <label class="form-group label"><strong>ZIMRA Verification URL</strong></label>
                        <div class="code-box">${escapeHtml(doc.qrCodeUrl)}</div>
                    </div>
                ` : ''}
            `;
        } catch (err) {
            body.innerHTML = `<div class="text-center text-danger p-3">Failed to load document details: ${escapeHtml(err.message)}</div>`;
        }
    }

    // Load Audit Logs (Paginated)
    async function loadAuditLogs() {
        const tbody = document.getElementById('audit-table-body');
        if (!tbody) return;

        tbody.innerHTML = `<tr><td colspan="5" class="text-center">Loading audit logs...</td></tr>`;

        try {
            const params = new URLSearchParams({
                page: state.auditPage,
                pageSize: state.auditPageSize
            });

            if (state.auditInvoiceFilter) params.append('invoiceNo', state.auditInvoiceFilter);
            if (state.auditEventTypeFilter) params.append('eventType', state.auditEventTypeFilter);
            if (state.auditFromDateFilter) params.append('fromDate', state.auditFromDateFilter);
            if (state.auditToDateFilter) params.append('toDate', state.auditToDateFilter);

            const res = await fetchWithAuth(`/api/v1/diagnostics/audit-log?${params.toString()}`, {
                headers: { 'X-API-Key': 'fdms-admin-key-secure-prod-placeholder' }
            });
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();

            let logs = [];
            let totalItems = 0;
            let currentPage = 1;
            let totalPages = 1;

            if (Array.isArray(data)) {
                logs = data;
                totalItems = logs.length;
                currentPage = 1;
                totalPages = 1;
            } else if (data && Array.isArray(data.items)) {
                logs = data.items;
                totalItems = data.totalItems ?? logs.length;
                currentPage = data.page ?? 1;
                totalPages = data.totalPages ?? 1;
            }

            state.auditTotalPages = totalPages;
            updateAuditPaginationUI(totalItems, currentPage, totalPages);

            if (!logs || logs.length === 0) {
                tbody.innerHTML = `<tr><td colspan="5" class="text-center">No matching audit events found.</td></tr>`;
                return;
            }

            tbody.innerHTML = logs.map(log => {
                const idShort = log.id ? log.id.substring(0, 8) : '--';
                const badgeClass = (log.httpStatusCode && log.httpStatusCode >= 400) || (log.errorCategory && log.errorCategory !== 'None') ? 'badge-failed' : 'badge-info';
                const typeText = log.eventType || log.endpoint || log.direction || 'SYSTEM';
                const detailsText = log.details || log.errorMessage || (log.endpoint ? `${log.direction || 'HTTP'} ${log.endpoint} ${log.httpStatusCode ? `[HTTP ${log.httpStatusCode}]` : ''} (${log.durationMs || 0}ms)` : 'System event recorded');

                return `
                    <tr>
                        <td>#${escapeHtml(idShort)}</td>
                        <td>${formatDate(log.timestamp)}</td>
                        <td><span class="badge ${badgeClass}">${escapeHtml(typeText)}</span></td>
                        <td>${log.invoiceNo ? `<strong>${escapeHtml(log.invoiceNo)}</strong>` : '--'}</td>
                        <td>${escapeHtml(detailsText)}</td>
                    </tr>
                `;
            }).join('');

        } catch (err) {
            console.error('Failed to load audit logs:', err);
            tbody.innerHTML = `<tr><td colspan="5" class="text-center text-danger">Failed to load audit logs: ${escapeHtml(err.message)}</td></tr>`;
        }
    }

    // Audit Logs Pagination UI Update
    function updateAuditPaginationUI(totalItems, currentPage, totalPages) {
        const info = document.getElementById('audit-pagination-info');
        const text = document.getElementById('audit-page-current-text');
        const btnPrev = document.getElementById('btn-audit-page-prev');
        const btnNext = document.getElementById('btn-audit-page-next');

        if (info) info.textContent = `Showing page ${currentPage} of ${totalPages} (${totalItems} total log entries)`;
        if (text) text.textContent = `Page ${currentPage} of ${totalPages}`;

        if (btnPrev) btnPrev.disabled = currentPage <= 1;
        if (btnNext) btnNext.disabled = currentPage >= totalPages;
    }

    // Show No Registered Device Modal
    function showNoDeviceModal(actionName = 'perform fiscal day operation') {
        const modal = document.getElementById('modal-no-device-error');
        const title = document.getElementById('no-device-modal-title');
        const message = document.getElementById('no-device-modal-message');

        if (title) title.textContent = `Cannot ${actionName.toUpperCase()}`;
        if (message) message.textContent = `A fiscal day cannot be opened or closed without an active registered fiscal device in the system.`;

        if (modal) {
            modal.classList.remove('hidden');
        } else {
            alert(`Registration Required: Cannot ${actionName} without a registered fiscal device.`);
        }
    }

    function hideNoDeviceModal() {
        document.getElementById('modal-no-device-error')?.classList.add('hidden');
    }

    // Handlers for Admin Quick Actions
    async function handleOpenDay() {
        const hasDevices = (state.devices && state.devices.length > 0) || (state.device && state.device.deviceId);
        if (!hasDevices) {
            showNoDeviceModal('Open Fiscal Day');
            return;
        }

        const deviceCount = (state.devices && state.devices.length > 0) ? state.devices.length : 1;
        const confirmMsg = deviceCount > 1
            ? `Are you sure you want to open Fiscal Day for ALL ${deviceCount} registered devices?`
            : `Are you sure you want to open Fiscal Day for Device ID ${state.device?.deviceId || 'registered device'}?`;

        if (!confirm(confirmMsg)) return;

        try {
            const res = await fetchWithAuth('/api/v1/admin/day/open', {
                method: 'POST',
                headers: { 'X-API-Key': 'fdms-admin-key-secure-prod-placeholder' }
            });
            const data = await res.json();

            if (!res.ok) {
                if (res.status === 404 || (data.detail && data.detail.includes('not registered')) || (data.error === 'NO_DEVICES')) {
                    showNoDeviceModal('Open Fiscal Day');
                    return;
                }
                throw new Error(data.detail || data.title || data.message || `HTTP ${res.status}`);
            }

            alert(data.message || 'Fiscal Day Opened Successfully for all registered devices.');
            loadAllData();
        } catch (err) {
            alert('Failed to open fiscal day: ' + err.message);
        }
    }

    async function handleCloseDay() {
        const hasDevices = (state.devices && state.devices.length > 0) || (state.device && state.device.deviceId);
        if (!hasDevices) {
            showNoDeviceModal('Close Fiscal Day');
            return;
        }

        const deviceCount = (state.devices && state.devices.length > 0) ? state.devices.length : 1;
        const confirmMsg = deviceCount > 1
            ? `Are you sure you want to close Fiscal Day for ALL ${deviceCount} registered devices? Further invoices will be blocked until re-opened.`
            : `Are you sure you want to close Fiscal Day for Device ID ${state.device?.deviceId || 'registered device'}? Further invoices will be blocked until re-opened.`;

        if (!confirm(confirmMsg)) return;

        try {
            const res = await fetchWithAuth('/api/v1/admin/day/close', {
                method: 'POST',
                headers: { 'X-API-Key': 'fdms-admin-key-secure-prod-placeholder' }
            });
            const data = await res.json();

            if (!res.ok) {
                if (res.status === 404 || (data.detail && data.detail.includes('not registered')) || (data.error === 'NO_DEVICES')) {
                    showNoDeviceModal('Close Fiscal Day');
                    return;
                }
                throw new Error(data.detail || data.title || data.message || `HTTP ${res.status}`);
            }

            alert(data.message || 'Fiscal Day Closed Successfully for all registered devices.');
            loadAllData();
        } catch (err) {
            alert('Failed to close fiscal day: ' + err.message);
        }
    }

    async function handleSyncConfig() {
        const hasDevices = (state.devices && state.devices.length > 0) || (state.device && state.device.deviceId);
        if (!hasDevices) {
            showNoDeviceModal('Sync ZIMRA Config');
            return;
        }

        try {
            const res = await fetchWithAuth('/api/v1/admin/device/sync-config', {
                method: 'POST',
                headers: { 'X-API-Key': 'fdms-admin-key-secure-prod-placeholder' }
            });
            const data = await res.json().catch(() => ({}));
            if (!res.ok) {
                if (res.status === 404 || (data.detail && data.detail.includes('not registered')) || (data.error === 'NO_DEVICES')) {
                    showNoDeviceModal('Sync ZIMRA Config');
                    return;
                }
                throw new Error(data.detail || data.title || `HTTP ${res.status}`);
            }
            alert(data.message || 'ZIMRA Device Configuration synced successfully for all registered devices.');
            loadAllData();
        } catch (err) {
            alert('Failed to sync config: ' + err.message);
        }
    }

    async function handleRetryQueue() {
        try {
            let url = '/api/v1/dashboard/queue/retry';
            if (state.queueFilters.deviceId) {
                url += `?deviceId=${encodeURIComponent(state.queueFilters.deviceId)}`;
            }
            const res = await fetchWithAuth(url, { method: 'POST' });
            const data = await res.json();
            alert(data.message || 'Queue retry triggered.');
            loadAllData();
        } catch (err) {
            alert('Failed to trigger queue retry: ' + err.message);
        }
    }

    async function handleDeviceRegistration(e) {
        e.preventDefault();
        const deviceId = parseInt(document.getElementById('reg-device-id').value, 10);
        const activationKey = document.getElementById('reg-activation-key').value.trim();
        const box = document.getElementById('reg-response-box');

        if (!deviceId || !activationKey) {
            alert('Please enter a valid Device ID and Activation Key.');
            return;
        }

        const btn = document.getElementById('btn-submit-registration');
        btn.disabled = true;
        btn.innerHTML = `<i class="fa-solid fa-spinner fa-spin"></i> Generating Keys & CSR...`;

        try {
            const res = await fetchWithAuth('/api/v1/admin/device/register', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'X-API-Key': 'fdms-admin-key-secure-prod-placeholder'
                },
                body: JSON.stringify({ deviceID: deviceId, activationKey: activationKey })
            });

            const data = await res.json();
            btn.disabled = false;
            btn.innerHTML = `<i class="fa-solid fa-certificate"></i> Register Device & Issue Certificate`;

            if (res.ok) {
                if (box) {
                    box.classList.remove('hidden');
                    box.className = 'response-box p-3 glass mt-3 text-success';
                    box.innerHTML = `<strong>Success!</strong> ${escapeHtml(data.message)}<br>Device Serial: ${escapeHtml(data.deviceSerialNo || '')}`;
                }
                loadAllData();
            } else {
                alert('Registration failed: ' + (data.error || data.message || 'Unknown error'));
            }
        } catch (err) {
            btn.disabled = false;
            btn.innerHTML = `<i class="fa-solid fa-certificate"></i> Register Device & Issue Certificate`;
            alert('Error submitting registration: ' + err.message);
        }
    }

    // Helper Utilities
    function getStatusBadgeClass(status) {
        if (status === 'Fiscalized' || status === 'Completed') return 'badge-success';
        if (status === 'Pending' || status === 'Queued') return 'badge-pending';
        return 'badge-failed';
    }

    function formatDate(dateStr) {
        if (!dateStr) return '--';
        const d = new Date(dateStr);
        return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    }

    function formatTime(dateStr) {
        if (!dateStr) return '--';
        const d = new Date(dateStr);
        return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
    }

    function escapeHtml(str) {
        if (typeof str !== 'string') return str;
        return str.replace(/[&<>"']/g, function(m) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#039;' }[m];
        });
    }

    // Load Subscription Details
    async function loadSubscriptionDetails() {
        try {
            const res = await fetchWithAuth('/api/v1/subscription/status');
            if (!res.ok) return;
            const data = await res.json();

            // Update Subscription Profile Cards
            const badge = document.getElementById('sub-status-badge');
            const navBadge = document.getElementById('nav-sub-badge');
            const planName = document.getElementById('sub-plan-name');
            const tin = document.getElementById('sub-taxpayer-tin');
            const validFrom = document.getElementById('sub-valid-from');
            const validTo = document.getElementById('sub-valid-to');
            const daysRemaining = document.getElementById('sub-days-remaining');
            const graceStatus = document.getElementById('sub-grace-status');
            const maxDevices = document.getElementById('sub-max-devices');
            const maskedKey = document.getElementById('sub-masked-key');

            if (planName) planName.textContent = data.planName || 'Unlicensed';
            if (tin) tin.textContent = data.taxPayerTIN || '--';
            if (validFrom) validFrom.textContent = formatDate(data.validFrom);
            if (validTo) validTo.textContent = formatDate(data.validTo);
            if (daysRemaining) daysRemaining.textContent = `${data.daysRemaining} Day(s)`;
            if (graceStatus) graceStatus.textContent = data.isInGracePeriod ? `Grace Period Active (${data.graceDaysRemaining} day(s) left)` : `Active until ${formatDate(data.gracePeriodEndsAt)}`;
            if (maxDevices) maxDevices.textContent = `${data.maxDevices} Devices`;
            if (maskedKey) maskedKey.textContent = data.maskedApiKey || 'FDMS-SUB-****';

            // Badges
            if (badge) {
                if (data.isHardExpired) {
                    badge.className = 'badge badge-failed';
                    badge.textContent = 'EXPIRED';
                } else if (data.isInGracePeriod) {
                    badge.className = 'badge badge-pending';
                    badge.textContent = 'GRACE PERIOD';
                } else {
                    badge.className = 'badge badge-success';
                    badge.textContent = 'ACTIVE';
                }
            }

            if (navBadge) {
                if (data.isHardExpired) {
                    navBadge.className = 'badge badge-failed';
                    navBadge.textContent = 'Expired';
                } else if (data.isInGracePeriod) {
                    navBadge.className = 'badge badge-pending';
                    navBadge.textContent = 'Grace';
                } else {
                    navBadge.className = 'badge badge-success';
                    navBadge.textContent = 'Active';
                }
            }

            // Warning Alert Box inside Tab
            const warningAlert = document.getElementById('sub-warning-alert-box');
            const warningMsg = document.getElementById('sub-warning-alert-msg');
            if (warningAlert && warningMsg) {
                if (data.warningMessage) {
                    warningAlert.classList.remove('hidden');
                    warningMsg.textContent = data.warningMessage;
                } else {
                    warningAlert.classList.add('hidden');
                }
            }

            // Global Top Header Warning Banner
            const globalBanner = document.getElementById('global-sub-warning-banner');
            const globalText = document.getElementById('global-sub-warning-text');
            const globalIcon = document.getElementById('global-sub-banner-icon');

            if (globalBanner && globalText) {
                if (data.isHardExpired) {
                    globalBanner.classList.remove('hidden');
                    globalBanner.style.background = 'rgba(239, 68, 68, 0.15)';
                    globalBanner.style.borderColor = 'rgba(239, 68, 68, 0.4)';
                    globalBanner.style.color = '#FCA5A5';
                    if (globalIcon) globalIcon.className = 'fa-solid fa-triangle-exclamation fa-lg text-danger';
                    globalText.innerHTML = `<strong>CRITICAL:</strong> Bridge Subscription Expired (Grace Period ended). Fiscal invoice submissions are blocked!`;
                } else if (data.isInGracePeriod) {
                    globalBanner.classList.remove('hidden');
                    globalBanner.style.background = 'rgba(245, 158, 11, 0.15)';
                    globalBanner.style.borderColor = 'rgba(245, 158, 11, 0.4)';
                    globalBanner.style.color = '#FCD34D';
                    if (globalIcon) globalIcon.className = 'fa-solid fa-triangle-exclamation fa-lg text-warning';
                    globalText.innerHTML = `<strong>WARNING:</strong> Subscription Expired. Operating in 7-day Grace Period (<strong>${data.graceDaysRemaining} day(s) remaining</strong>). Please renew immediately.`;
                } else if (data.daysRemaining <= 14) {
                    globalBanner.classList.remove('hidden');
                    globalBanner.style.background = 'rgba(245, 158, 11, 0.15)';
                    globalBanner.style.borderColor = 'rgba(245, 158, 11, 0.4)';
                    globalBanner.style.color = '#FCD34D';
                    if (globalIcon) globalIcon.className = 'fa-solid fa-circle-info fa-lg text-warning';
                    globalText.innerHTML = `<strong>NOTICE:</strong> Subscription expires in <strong>${data.daysRemaining} day(s)</strong> on ${formatDate(data.validTo)}.`;
                } else {
                    globalBanner.classList.add('hidden');
                }
            }

        } catch (err) {
            console.error('Failed to load subscription status:', err);
        }
    }

    // Activate Renewed Subscription Handler
    async function handleActivateSubscription(e) {
        e.preventDefault();
        const apiKey = document.getElementById('input-renew-api-key').value.trim();
        const box = document.getElementById('sub-renewal-response-box');
        const btn = document.getElementById('btn-submit-renewal');

        if (!apiKey) {
            alert('Please paste a valid subscription API key string.');
            return;
        }

        btn.disabled = true;
        btn.innerHTML = `<i class="fa-solid fa-spinner fa-spin"></i> Verifying Cryptographic Signature...`;

        try {
            const res = await fetchWithAuth('/api/v1/subscription/activate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ apiKey: apiKey })
            });

            const data = await res.json();
            btn.disabled = false;
            btn.innerHTML = `<i class="fa-solid fa-certificate"></i> Validate & Activate Renewed Key`;

            if (res.ok) {
                if (box) {
                    box.classList.remove('hidden');
                    box.className = 'response-box p-3 glass mt-3 text-success';
                    box.innerHTML = `<strong>Subscription Activated!</strong> Plan: <strong>${escapeHtml(data.planName)}</strong> | Valid Until: <strong>${formatDate(data.validTo)}</strong>`;
                }
                document.getElementById('input-renew-api-key').value = '';
                await loadSubscriptionDetails();
            } else {
                if (box) {
                    box.classList.remove('hidden');
                    box.className = 'response-box p-3 glass mt-3 text-danger';
                    box.innerHTML = `<strong>Activation Failed:</strong> ${escapeHtml(data.message || data.error || 'Invalid API Key')}`;
                }
            }
        } catch (err) {
            btn.disabled = false;
            btn.innerHTML = `<i class="fa-solid fa-certificate"></i> Validate & Activate Renewed Key`;
            alert('Error activating subscription: ' + err.message);
        }
    }

    // Developer Test Key Generator Handler
    async function handleGenerateDevKey() {
        const tin = document.getElementById('gen-key-tin').value.trim() || '2000123456';
        const plan = document.getElementById('gen-key-plan').value;
        const months = parseInt(document.getElementById('gen-key-months').value, 10) || 12;

        try {
            const res = await fetchWithAuth('/api/v1/subscription/generate-dev-key', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    taxPayerTIN: tin,
                    planName: plan,
                    durationMonths: months
                })
            });

            const data = await res.json();
            if (res.ok) {
                document.getElementById('input-renew-api-key').value = data.apiKey;
                const resultBox = document.getElementById('gen-key-result-box');
                const codeElem = document.getElementById('gen-key-output-code');
                if (resultBox && codeElem) {
                    resultBox.classList.remove('hidden');
                    codeElem.textContent = data.apiKey;
                }
            } else {
                alert('Failed to generate key: ' + (data.message || 'Error'));
            }
        } catch (err) {
            alert('Error generating test key: ' + err.message);
        }
    }

    function copyGenKeyToClipboard() {
        const keyText = document.getElementById('gen-key-output-code')?.textContent;
        if (keyText && keyText !== '--') {
            navigator.clipboard.writeText(keyText);
            alert('Generated subscription key copied to clipboard!');
        }
    }

    // Helper to append session token to URL for direct browser navigation (window.open)
    function appendAuthTokenToUrl(url) {
        if (!url) return url;
        if (!state.authToken) return url;
        const separator = url.includes('?') ? '&' : '?';
        return `${url}${separator}token=${encodeURIComponent(state.authToken)}`;
    }

    let dirBrowserState = {
        currentPath: '',
        parentPath: null,
        drives: []
    };

    // REPORTS TAB EVENT LISTENERS AND HANDLERS
    function setupReportsEventListeners() {
        document.getElementById('form-reports-storage-config')?.addEventListener('submit', handleSaveReportsStorage);
        document.getElementById('btn-reset-storage-path')?.addEventListener('click', () => {
            const input = document.getElementById('input-reports-storage-path');
            if (input) input.value = 'C:\\FDMS\\ZReports';
        });
        document.getElementById('btn-browse-reports-storage')?.addEventListener('click', openDirectoryBrowserModal);
        document.getElementById('btn-close-browse-modal')?.addEventListener('click', closeDirectoryBrowserModal);
        document.getElementById('btn-cancel-browse-modal')?.addEventListener('click', closeDirectoryBrowserModal);
        document.getElementById('btn-confirm-browse-dir')?.addEventListener('click', confirmDirectorySelection);
        document.getElementById('btn-dir-up-level')?.addEventListener('click', () => {
            if (dirBrowserState.parentPath) {
                loadDirectoryPath(dirBrowserState.parentPath);
            }
        });
        document.getElementById('btn-generate-xreport')?.addEventListener('click', handleGenerateXReport);
        document.getElementById('btn-refresh-zreports-table')?.addEventListener('click', () => {
            state.zreportsPage = 1;
            loadZReportsTable();
        });
        document.getElementById('btn-zreports-page-prev')?.addEventListener('click', () => {
            if (state.zreportsPage > 1) {
                state.zreportsPage--;
                loadZReportsTable();
            }
        });
        document.getElementById('btn-zreports-page-next')?.addEventListener('click', () => {
            if (state.zreportsPage < state.zreportsTotalPages) {
                state.zreportsPage++;
                loadZReportsTable();
            }
        });
    }

    async function openDirectoryBrowserModal() {
        const modal = document.getElementById('modal-browse-directory');
        if (!modal) return;
        modal.classList.remove('hidden');

        const currentInput = document.getElementById('input-reports-storage-path');
        const initialPath = currentInput ? currentInput.value.trim() : '';
        await loadDirectoryPath(initialPath);
    }

    function closeDirectoryBrowserModal() {
        const modal = document.getElementById('modal-browse-directory');
        if (modal) modal.classList.add('hidden');
    }

    function confirmDirectorySelection() {
        const input = document.getElementById('input-reports-storage-path');
        if (input && dirBrowserState.currentPath) {
            input.value = dirBrowserState.currentPath;
        }
        closeDirectoryBrowserModal();
    }

    async function loadDirectoryPath(path) {
        const pathInput = document.getElementById('dir-current-path-input');
        const drivesBar = document.getElementById('dir-drives-bar');
        const listContainer = document.getElementById('dir-list-container');
        const errBox = document.getElementById('dir-browse-error');
        const upBtn = document.getElementById('btn-dir-up-level');

        if (errBox) errBox.classList.add('hidden');
        if (listContainer) {
            listContainer.innerHTML = `<div class="text-center text-muted p-3"><i class="fa-solid fa-spinner fa-spin"></i> Loading...</div>`;
        }

        try {
            const url = path ? `/api/v1/reports/browse-dirs?path=${encodeURIComponent(path)}` : '/api/v1/reports/browse-dirs';
            const res = await fetchWithAuth(url);
            if (!res.ok) {
                const errData = await res.json();
                throw new Error(errData.message || `HTTP ${res.status}`);
            }

            const data = await res.json();
            dirBrowserState.currentPath = data.currentPath;
            dirBrowserState.parentPath = data.parentPath;
            dirBrowserState.drives = data.drives || [];

            if (pathInput) pathInput.value = data.currentPath;
            if (upBtn) upBtn.disabled = !data.parentPath;

            // Render Drives
            if (drivesBar) {
                drivesBar.innerHTML = dirBrowserState.drives.map(drive => `
                    <button type="button" class="btn ${data.currentPath.toLowerCase().startsWith(drive.toLowerCase()) ? 'btn-primary' : 'btn-secondary'} btn-sm btn-drive" data-path="${escapeHtml(drive)}">
                        <i class="fa-solid fa-hard-drive"></i> ${escapeHtml(drive)}
                    </button>
                `).join('');

                drivesBar.querySelectorAll('.btn-drive').forEach(btn => {
                    btn.addEventListener('click', () => {
                        const drivePath = btn.getAttribute('data-path');
                        if (drivePath) loadDirectoryPath(drivePath);
                    });
                });
            }

            // Render Subdirectories
            if (listContainer) {
                if (!data.directories || data.directories.length === 0) {
                    listContainer.innerHTML = `<div class="text-center text-muted p-3" style="font-size: 12px;">No subdirectories found in this folder.</div>`;
                    return;
                }

                listContainer.innerHTML = data.directories.map(dir => `
                    <div class="dir-item p-2" data-path="${escapeHtml(dir.fullPath)}" style="display: flex; align-items: center; gap: 8px; cursor: pointer; border-radius: var(--radius-sm); font-size: 12px; margin-bottom: 2px;">
                        <i class="fa-solid fa-folder text-warning" style="font-size: 14px;"></i>
                        <span style="font-family: var(--font-mono); font-weight: 500;">${escapeHtml(dir.name)}</span>
                    </div>
                `).join('');

                listContainer.querySelectorAll('.dir-item').forEach(item => {
                    item.addEventListener('mouseenter', () => { item.style.background = 'rgba(99, 102, 241, 0.15)'; });
                    item.addEventListener('mouseleave', () => { item.style.background = 'transparent'; });
                    item.addEventListener('dblclick', () => {
                        const target = item.getAttribute('data-path');
                        if (target) loadDirectoryPath(target);
                    });
                    item.addEventListener('click', () => {
                        const target = item.getAttribute('data-path');
                        if (target) {
                            dirBrowserState.currentPath = target;
                            if (pathInput) pathInput.value = target;
                        }
                    });
                });
            }
        } catch (err) {
            console.error('Directory browse error:', err);
            if (errBox) {
                errBox.classList.remove('hidden');
                errBox.textContent = 'Failed to open directory: ' + err.message;
            }
            if (listContainer) {
                listContainer.innerHTML = `<div class="text-center text-danger p-3" style="font-size: 12px;"><i class="fa-solid fa-triangle-exclamation"></i> Access denied or invalid folder.</div>`;
            }
        }
    }

    async function loadReportsConfig() {
        const input = document.getElementById('input-reports-storage-path');
        const badge = document.getElementById('reports-path-status-badge');
        if (!input) return;

        try {
            const res = await fetchWithAuth('/api/v1/reports/config');
            if (!res.ok) return;
            const data = await res.json();
            input.value = data.storagePath || '';
            if (badge) {
                badge.className = `badge ${data.isPathValid ? 'badge-success' : 'badge-failed'}`;
                badge.textContent = data.isPathValid ? 'Directory Active' : 'Invalid Path';
            }
        } catch (err) {
            console.error('Failed to load reports storage config:', err);
        }
    }

    async function handleSaveReportsStorage(e) {
        if (e) e.preventDefault();
        const input = document.getElementById('input-reports-storage-path');
        const btn = document.getElementById('btn-save-reports-storage');
        const box = document.getElementById('reports-storage-response-box');
        const badge = document.getElementById('reports-path-status-badge');

        const path = input ? input.value.trim() : '';
        if (!path) {
            alert('Please enter a target storage directory path.');
            return;
        }

        if (btn) btn.disabled = true;
        try {
            const res = await fetchWithAuth('/api/v1/reports/config', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ storagePath: path })
            });

            const data = await res.json();
            if (btn) btn.disabled = false;

            if (res.ok && data.isPathValid) {
                if (badge) {
                    badge.className = 'badge badge-success';
                    badge.textContent = 'Directory Active';
                }
                if (box) {
                    box.classList.remove('hidden');
                    box.className = 'response-box p-3 glass mt-3 text-success';
                    box.innerHTML = `<strong>Success!</strong> ${escapeHtml(data.message || 'Storage location updated successfully.')}`;
                }
            } else {
                if (badge) {
                    badge.className = 'badge badge-failed';
                    badge.textContent = 'Invalid Path';
                }
                if (box) {
                    box.classList.remove('hidden');
                    box.className = 'response-box p-3 glass mt-3 text-danger';
                    box.innerHTML = `<strong>Error:</strong> ${escapeHtml(data.message || 'Failed to update storage directory.')}`;
                }
            }
        } catch (err) {
            if (btn) btn.disabled = false;
            alert('Error updating storage path: ' + err.message);
        }
    }

    async function handleGenerateXReport() {
        const btn = document.getElementById('btn-generate-xreport');
        const box = document.getElementById('xreport-response-box');
        const devId = state.device?.deviceId || 1042;

        if (btn) btn.disabled = true;
        if (box) {
            box.classList.remove('hidden');
            box.className = 'response-box p-3 glass mt-3 text-info';
            box.innerHTML = `<i class="fa-solid fa-spinner fa-spin"></i> Calculating open day totals and generating X-Report PDF...`;
        }

        try {
            const res = await fetchWithAuth(`/api/v1/reports/x-report?deviceId=${devId}`, { method: 'POST' });
            const data = await res.json();
            if (btn) btn.disabled = false;

            if (res.ok) {
                const rawPdfUrl = data.pdfDownloadUrl || `/api/v1/reports/x-report/pdf?deviceId=${devId}`;
                const pdfUrl = appendAuthTokenToUrl(rawPdfUrl);

                if (box) {
                    box.className = 'response-box p-3 glass mt-3 text-success';
                    box.innerHTML = `
                        <div style="display: flex; justify-content: space-between; align-items: center; flex-wrap: wrap; gap: 10px;">
                            <div>
                                <strong><i class="fa-solid fa-circle-check"></i> Interim X-Report Generated!</strong><br>
                                <span style="font-size: 11px; color: var(--text-muted);">
                                    Fiscal Day #${data.fiscalDayNo} • Receipts: ${data.startingReceiptGlobalNo} - ${data.endingReceiptGlobalNo} (${data.totalFiscalizedDocuments} docs)
                                </span>
                            </div>
                            <a href="${pdfUrl}" target="_blank" class="btn btn-secondary btn-sm" style="text-decoration: none;">
                                <i class="fa-solid fa-arrow-up-right-from-square"></i> Open PDF Report
                            </a>
                        </div>
                    `;
                }

                // Automatically launch PDF view in new browser tab
                window.open(pdfUrl, '_blank');
            } else {
                if (box) {
                    box.className = 'response-box p-3 glass mt-3 text-danger';
                    box.innerHTML = `<strong>Error:</strong> ${escapeHtml(data.message || 'Failed to generate X-Report.')}`;
                }
            }
        } catch (err) {
            if (btn) btn.disabled = false;
            if (box) {
                box.className = 'response-box p-3 glass mt-3 text-danger';
                box.innerHTML = `<strong>Connection Error:</strong> ${escapeHtml(err.message)}`;
            }
        }
    }

    async function loadZReportsTable() {
        const tbody = document.getElementById('zreports-table-body');
        if (!tbody) return;

        const devId = state.device?.deviceId || 0;
        try {
            const res = await fetchWithAuth(`/api/v1/reports/z-reports?deviceId=${devId}&page=${state.zreportsPage}&pageSize=${state.zreportsPageSize}`);
            if (!res.ok) throw new Error(`HTTP ${res.status}`);
            const data = await res.json();
            let list = [];
            let totalItems = 0;
            let currentPage = 1;
            let totalPages = 1;

            if (Array.isArray(data)) {
                list = data;
                totalItems = list.length;
                currentPage = 1;
                totalPages = 1;
            } else if (data && Array.isArray(data.items)) {
                list = data.items;
                totalItems = data.totalItems ?? list.length;
                currentPage = data.page ?? 1;
                totalPages = data.totalPages ?? 1;
            }

            state.zreportsTotalPages = totalPages;
            updateZReportsPaginationUI(totalItems, currentPage, totalPages);

            if (!list || list.length === 0) {
                tbody.innerHTML = `<tr><td colspan="9" class="text-center">No closed fiscal day Z-Reports recorded yet. Close a fiscal day to generate a Z-Report.</td></tr>`;
                return;
            }

            tbody.innerHTML = list.map(z => `
                <tr>
                    <td><strong>Day #${z.fiscalDayNo}</strong></td>
                    <td>${formatDate(z.reportDate)}</td>
                    <td>#${z.startingReceiptGlobalNo} - #${z.endingReceiptGlobalNo}</td>
                    <td><span class="badge badge-info">${z.totalFiscalizedDocuments} docs</span></td>
                    <td>$${(z.totalUsdSales || 0).toFixed(2)}</td>
                    <td>ZiG ${(z.totalZigSales || 0).toFixed(2)}</td>
                    <td style="font-family: var(--font-mono); font-size: 11px;">${escapeHtml(z.pdfFileName)}</td>
                    <td>${formatTime(z.generatedAtUtc)}</td>
                    <td>
                        <button type="button" class="btn btn-secondary btn-sm btn-view-zreport-pdf" data-url="${escapeHtml(z.pdfViewUrl)}" title="Open PDF in New Browser Tab">
                            <i class="fa-solid fa-eye"></i> View PDF
                        </button>
                    </td>
                </tr>
            `).join('');

            tbody.querySelectorAll('.btn-view-zreport-pdf').forEach(btn => {
                btn.addEventListener('click', () => {
                    const rawUrl = btn.getAttribute('data-url');
                    if (rawUrl) {
                        const pdfUrl = appendAuthTokenToUrl(rawUrl);
                        window.open(pdfUrl, '_blank');
                    }
                });
            });

        } catch (err) {
            console.error('Failed to load Z-Reports list:', err);
            tbody.innerHTML = `<tr><td colspan="9" class="text-center text-danger">Failed to load Z-Reports ledger.</td></tr>`;
        }
    }

    function updateZReportsPaginationUI(totalItems, currentPage, totalPages) {
        const info = document.getElementById('zreports-pagination-info');
        const text = document.getElementById('zreports-page-current-text');
        const btnPrev = document.getElementById('btn-zreports-page-prev');
        const btnNext = document.getElementById('btn-zreports-page-next');

        if (info) info.textContent = `Showing page ${currentPage} of ${totalPages} (${totalItems} total Z-Reports)`;
        if (text) text.textContent = `Page ${currentPage} of ${totalPages}`;

        if (btnPrev) btnPrev.disabled = currentPage <= 1;
        if (btnNext) btnNext.disabled = currentPage >= totalPages;
    }

    // WHITE-LABEL BRANDING MANAGEMENT
    async function loadBranding() {
        try {
            const res = await fetch('/api/v1/branding');
            if (!res.ok) return;
            const branding = await res.json();
            renderBrandingUI(branding);
        } catch (err) {
            console.error('Failed to load white-label branding:', err);
        }
    }

    function renderBrandingUI(branding) {
        if (!branding) return;

        // Company Name in Sidebar Header
        const companyNameElem = document.getElementById('brand-company-name');
        if (companyNameElem && branding.companyName) {
            companyNameElem.textContent = branding.companyName;
        }

        // Company Name in Login Modal
        const loginCompanyElem = document.getElementById('login-company-name-text');
        if (loginCompanyElem && branding.companyName) {
            loginCompanyElem.textContent = branding.companyName;
        }

        // Admin Panel Branding Form Company Name
        const adminCompanyInput = document.getElementById('input-branding-company-name');
        if (adminCompanyInput && branding.companyName) {
            adminCompanyInput.value = branding.companyName;
        }

        // Admin Panel Branding Preview Name
        const previewNameElem = document.getElementById('branding-preview-name-text');
        if (previewNameElem && branding.companyName) {
            previewNameElem.textContent = branding.companyName;
        }

        // Branding Status Badge
        const statusBadge = document.getElementById('branding-status-badge');
        if (statusBadge) {
            statusBadge.textContent = branding.hasCustomLogo ? 'Custom Logo Active' : 'Default Branding';
            statusBadge.className = branding.hasCustomLogo ? 'badge badge-success' : 'badge badge-info';
        }

        // Sidebar Logo Handling
        const sidebarLogoIcon = document.getElementById('brand-logo-icon');
        const sidebarLogoImg = document.getElementById('brand-logo-img');
        if (sidebarLogoIcon && sidebarLogoImg) {
            if (branding.hasCustomLogo && branding.logoUrl) {
                sidebarLogoImg.src = branding.logoUrl;
                sidebarLogoImg.classList.remove('hidden');
                sidebarLogoIcon.classList.add('hidden');
            } else {
                sidebarLogoImg.classList.add('hidden');
                sidebarLogoIcon.classList.remove('hidden');
            }
        }

        // Login Modal Logo Handling
        const loginLogoIcon = document.getElementById('login-brand-logo-icon');
        const loginLogoImg = document.getElementById('login-brand-logo-img');
        if (loginLogoIcon && loginLogoImg) {
            if (branding.hasCustomLogo && branding.logoUrl) {
                loginLogoImg.src = branding.logoUrl;
                loginLogoImg.classList.remove('hidden');
                loginLogoIcon.classList.add('hidden');
            } else {
                loginLogoImg.classList.add('hidden');
                loginLogoIcon.classList.remove('hidden');
            }
        }

        // Company Name in Registration Modal
        const registerCompanyElem = document.getElementById('register-company-name-text');
        if (registerCompanyElem && branding.companyName) {
            registerCompanyElem.textContent = branding.companyName;
        }

        // Registration Modal Logo Handling
        const registerLogoIcon = document.getElementById('register-brand-logo-icon');
        const registerLogoImg = document.getElementById('register-brand-logo-img');
        if (registerLogoIcon && registerLogoImg) {
            if (branding.hasCustomLogo && branding.logoUrl) {
                registerLogoImg.src = branding.logoUrl;
                registerLogoImg.classList.remove('hidden');
                registerLogoIcon.classList.add('hidden');
            } else {
                registerLogoImg.classList.add('hidden');
                registerLogoIcon.classList.remove('hidden');
            }
        }
    }

    function escapeHtml(str) {
        if (!str) return '';
        return String(str).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    // USERS & PERMISSIONS MANAGEMENT HANDLERS
    let cachedAdminUsers = [];

    async function loadUsersTab() {
        const tbody = document.getElementById('admin-users-table-body');
        if (!tbody) return;

        try {
            const res = await fetchWithAuth('/api/v1/tenant/users');
            if (!res.ok) {
                if (res.status === 403) {
                    tbody.innerHTML = `<tr><td colspan="6" class="text-center" style="padding: 2rem; color: var(--text-muted);"><i class="fa-solid fa-lock" style="margin-right: 6px;"></i>Insufficient permissions to view user directory. Contact your Tenant Admin.</td></tr>`;
                    return;
                }
                if (res.status === 401) {
                    if (!state.isAuthenticated) {
                        showPinLoginModal();
                    } else {
                        tbody.innerHTML = `<tr><td colspan="6" class="text-center" style="padding: 2rem; color: var(--text-muted);"><i class="fa-solid fa-shield-halved" style="margin-right: 6px;"></i>Session validation failed. Please re-authenticate using the sidebar Lock button.</td></tr>`;
                    }
                    return;
                }
                tbody.innerHTML = `<tr><td colspan="6" class="text-center" style="padding: 2rem; color: var(--text-muted);">Unable to load user directory (${res.status}). Ensure system session is active.</td></tr>`;
                return;
            }

            const users = await res.json();
            cachedAdminUsers = users || [];

            if (cachedAdminUsers.length === 0) {
                tbody.innerHTML = `<tr><td colspan="6" class="text-center" style="padding: 2rem; color: var(--text-muted);">No tenant user accounts registered yet. Click "Add User" to create one.</td></tr>`;
                return;
            }

            tbody.innerHTML = cachedAdminUsers.map(u => `
                <tr>
                    <td style="font-weight: 600;">${escapeHtml(u.fullName)}</td>
                    <td>${escapeHtml(u.email)} ${!u.isActive ? '<span class="badge badge-danger ml-2" style="font-size: 0.7rem;">Deactivated</span>' : ''}</td>
                    <td><span class="badge badge-primary">${escapeHtml(u.role)}</span></td>
                    <td><small style="color: var(--text-muted);">${(u.permissions || []).join(', ')}</small></td>
                    <td>${u.lastLoginAt ? new Date(u.lastLoginAt).toLocaleString() : 'Never'}</td>
                    <td style="text-align: right;">
                        <button class="btn btn-sm btn-outline-primary mr-1" onclick="openAdminEditUserModal(${u.id})" style="padding: 4px 8px; font-size: 0.75rem; display: inline-flex; align-items: center; gap: 4px;">
                            <i class="fa-solid fa-user-pen"></i> Permissions
                        </button>
                        <button class="btn btn-sm btn-outline-danger" onclick="deleteAdminUser(${u.id})" style="padding: 4px 8px; font-size: 0.75rem;">
                            <i class="fa-solid fa-trash"></i>
                        </button>
                    </td>
                </tr>
            `).join('');
        } catch (err) {
            console.error('Error loading users tab:', err);
            tbody.innerHTML = `<tr><td colspan="6" class="text-center" style="padding: 2rem; color: var(--text-muted);">Connection error while loading users directory.</td></tr>`;
        }
    }

    window.openAdminEditUserModal = function(userId) {
        const user = cachedAdminUsers.find(u => u.id === userId);
        if (!user) return;

        document.getElementById('admin-edit-user-id').value = user.id;
        document.getElementById('admin-edit-user-email').value = `${user.fullName} (${user.email})`;
        document.getElementById('admin-edit-user-role').value = user.role || 'TenantOperator';
        document.getElementById('admin-edit-user-active').value = user.isActive ? 'true' : 'false';

        const perms = user.permissions || [];
        document.getElementById('admin-edit-perm-dash').checked = perms.includes('ViewDashboard');
        document.getElementById('admin-edit-perm-docs').checked = perms.includes('ViewDocuments');
        document.getElementById('admin-edit-perm-devs').checked = perms.includes('ManageDevices');
        document.getElementById('admin-edit-perm-users').checked = perms.includes('ManageUsers');

        const errBox = document.getElementById('admin-edit-user-error');
        if (errBox) errBox.classList.add('hidden');

        document.getElementById('modal-admin-edit-user')?.classList.remove('hidden');
    };

    window.deleteAdminUser = async function(userId) {
        if (!confirm('Are you sure you want to remove this tenant user account?')) return;
        try {
            const res = await fetchWithAuth(`/api/v1/tenant/users/${userId}`, { method: 'DELETE' });
            if (res.ok) {
                loadUsersTab();
            } else {
                alert('Failed to remove tenant user.');
            }
        } catch (err) {
            alert('Error removing user: ' + err.message);
        }
    };

    async function handleCreateAdminUser(e) {
        e.preventDefault();
        const fullName = document.getElementById('admin-add-user-name').value.trim();
        const email = document.getElementById('admin-add-user-email').value.trim();
        const password = document.getElementById('admin-add-user-password').value;
        const role = document.getElementById('admin-add-user-role').value;
        const errBox = document.getElementById('admin-add-user-error');

        const perms = [];
        if (document.getElementById('admin-add-perm-dash').checked) perms.push('ViewDashboard');
        if (document.getElementById('admin-add-perm-docs').checked) perms.push('ViewDocuments');
        if (document.getElementById('admin-add-perm-devs').checked) perms.push('ManageDevices');
        if (document.getElementById('admin-add-perm-users').checked) perms.push('ManageUsers');

        if (errBox) errBox.classList.add('hidden');

        try {
            const res = await fetchWithAuth('/api/v1/tenant/users', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ fullName, email, password, role, permissions: perms })
            });
            const data = await res.json();
            if (res.ok) {
                document.getElementById('modal-admin-add-user')?.classList.add('hidden');
                loadUsersTab();
            } else {
                if (errBox) {
                    errBox.textContent = data.message || 'Failed to create tenant user.';
                    errBox.classList.remove('hidden');
                }
            }
        } catch (err) {
            if (errBox) {
                errBox.textContent = 'Error creating user: ' + err.message;
                errBox.classList.remove('hidden');
            }
        }
    }

    async function handleUpdateAdminUserPermissions(e) {
        e.preventDefault();
        const userId = document.getElementById('admin-edit-user-id').value;
        const role = document.getElementById('admin-edit-user-role').value;
        const isActive = document.getElementById('admin-edit-user-active').value === 'true';
        const errBox = document.getElementById('admin-edit-user-error');

        const perms = [];
        if (document.getElementById('admin-edit-perm-dash').checked) perms.push('ViewDashboard');
        if (document.getElementById('admin-edit-perm-docs').checked) perms.push('ViewDocuments');
        if (document.getElementById('admin-edit-perm-devs').checked) perms.push('ManageDevices');
        if (document.getElementById('admin-edit-perm-users').checked) perms.push('ManageUsers');

        if (errBox) errBox.classList.add('hidden');

        try {
            const res = await fetchWithAuth(`/api/v1/tenant/users/${userId}/permissions`, {
                method: 'PUT',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ role, permissions: perms, isActive })
            });
            const data = await res.json();
            if (res.ok) {
                document.getElementById('modal-admin-edit-user')?.classList.add('hidden');
                loadUsersTab();
            } else {
                if (errBox) {
                    errBox.textContent = data.message || 'Failed to update user permissions.';
                    errBox.classList.remove('hidden');
                }
            }
        } catch (err) {
            if (errBox) {
                errBox.textContent = 'Error updating user permissions: ' + err.message;
                errBox.classList.remove('hidden');
            }
        }
    }
});
