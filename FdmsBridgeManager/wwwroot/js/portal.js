document.addEventListener('DOMContentLoaded', () => {
    loadBranding();
    loadPlans();
    loadHardwareTiers();
    setupFormListeners();
});

let currentPlans = [];
let hardwareTiers = [];
let currentTransactionId = null;
let currentPayNowRef = null;

// Product View Tab Switcher
function switchProductTab(tab) {
    const btnPlans = document.getElementById('btn-tab-plans');
    const btnUpgrades = document.getElementById('btn-tab-upgrades');
    const viewPlans = document.getElementById('view-plans');
    const viewUpgrades = document.getElementById('view-upgrades');

    cancelCheckout();

    if (tab === 'upgrades') {
        btnPlans.classList.remove('active');
        btnUpgrades.classList.add('active');
        viewPlans.classList.add('hidden');
        viewUpgrades.classList.remove('hidden');
    } else {
        btnUpgrades.classList.remove('active');
        btnPlans.classList.add('active');
        viewUpgrades.classList.add('hidden');
        viewPlans.classList.remove('hidden');
    }
}

// 1. Fetch White Label Settings & Apply Live Dynamic Styling
async function loadBranding() {
    try {
        const res = await fetch('/api/v1/public/branding');
        if (!res.ok) return;
        const data = await res.json();

        // Inject Dynamic Colors into CSS Variables
        if (data.primaryColor) {
            document.documentElement.style.setProperty('--primary-color', data.primaryColor);
        }
        if (data.secondaryColor) {
            document.documentElement.style.setProperty('--secondary-color', data.secondaryColor);
        }

        // Update Text & Logos
        document.title = `${data.firmName} - FDMS License Portal`;
        document.getElementById('firm-name').innerText = data.firmName || 'Apex Fiscal Consultancy';
        document.getElementById('firm-tagline').innerText = data.tagline || 'Enterprise FDMS Bridge';
        document.getElementById('firm-email').innerText = data.supportEmail || 'support@apexconsultancy.co.zw';
        document.getElementById('firm-phone').innerText = data.supportPhone || '+263 77 123 4567';
        document.getElementById('firm-copyright').innerText = data.copyrightText || '© 2026 Apex Fiscal Consultancy Ltd.';
        document.getElementById('firm-address').innerText = data.address || '';

        if (data.logoUrl) {
            document.getElementById('firm-logo').src = data.logoUrl;
        }
    } catch (err) {
        console.warn('Failed to load dynamic branding config:', err);
    }
}

// 2. Fetch Active Dynamic Plans & Render Pricing Cards
async function loadPlans() {
    const grid = document.getElementById('plans-grid');
    try {
        const res = await fetch('/api/v1/public/plans');
        if (!res.ok) throw new Error('Failed to fetch subscription plans');
        currentPlans = await res.json();

        if (currentPlans.length === 0) {
            grid.innerHTML = '<div class="no-plans">No active subscription plans available.</div>';
            return;
        }

        grid.innerHTML = currentPlans.map((plan, index) => {
            const isFeatured = index === 1 || plan.PlanCode === 'PLAN_QUARTERLY' || plan.PlanCode === 'PLAN_ANNUAL';
            const badgeHtml = isFeatured ? '<span class="badge-featured">Popular Choice</span>' : '';
            const featuredClass = isFeatured ? 'featured' : '';

            return `
                <div class="plan-card ${featuredClass}">
                    ${badgeHtml}
                    <div class="plan-header">
                        <h4>${escapeHtml(plan.planName)}</h4>
                        <p>${escapeHtml(plan.description)}</p>
                    </div>
                    <div class="plan-price">
                        <span class="amount">$${plan.price.toFixed(2)}</span>
                        <span class="cycle"> / device / ${escapeHtml(plan.billingCycle)} (${plan.currency})</span>
                    </div>
                    <ul class="plan-features">
                        <li><i class="fa-solid fa-check"></i> Scalable <strong>Per Device</strong> Licensing</li>
                        <li><i class="fa-solid fa-check"></i> <strong>${plan.gracePeriodDays}-Day</strong> Operational Grace Period</li>
                        <li><i class="fa-solid fa-check"></i> Full ZIMRA TaRMS Compliance</li>
                        <li><i class="fa-solid fa-check"></i> Instant Automated Key Dispatch</li>
                    </ul>
                    <button class="btn-select-plan" onclick="selectPlan('${plan.id}')">
                        Select ${escapeHtml(plan.planName)}
                    </button>
                </div>
            `;
        }).join('');
    } catch (err) {
        console.error('Error loading plans:', err);
        grid.innerHTML = `<div class="error-msg">Error loading plans: ${err.message}</div>`;
    }
}

// Fetch Public Hardware Tier Upgrade Pricing Configuration
async function loadHardwareTiers() {
    const grid = document.getElementById('upgrades-grid');
    if (!grid) return;

    try {
        const res = await fetch('/api/v1/public/hardware-tiers');
        if (!res.ok) throw new Error('Failed to fetch hardware tier rates');
        hardwareTiers = await res.json();

        if (!hardwareTiers || hardwareTiers.length === 0) {
            hardwareTiers = [
                { tierName: 'TillStick', pricePerSlotUSD: 50.00, hardCap: 5, description: 'Single till POS USB bridge dongle' },
                { tierName: 'BranchBox', pricePerSlotUSD: 150.00, hardCap: 25, description: 'Multi-register branch server gateway appliance' },
                { tierName: 'Cloud', pricePerSlotUSD: 300.00, hardCap: 100, description: 'Elastic cloud-hosted enterprise gateway cluster' }
            ];
        }

        grid.innerHTML = hardwareTiers.map((tier) => {
            const isCloud = tier.tierName.toLowerCase().includes('cloud');
            const isBranch = tier.tierName.toLowerCase().includes('branch');
            const iconClass = isCloud ? 'fa-cloud' : (isBranch ? 'fa-server' : 'fa-usb');
            const featuredClass = isCloud || isBranch ? 'featured' : '';
            const badgeHtml = isCloud ? '<span class="badge-featured" style="background:#7c3aed;">Cloud Enterprise</span>' : (isBranch ? '<span class="badge-featured">Enterprise Server</span>' : '<span class="badge-featured" style="background:#0284c7;">Single Counter</span>');

            return `
                <div class="plan-card ${featuredClass}">
                    ${badgeHtml}
                    <div class="plan-header">
                        <h4><i class="fa-solid ${iconClass}" style="color: var(--primary-color);"></i> ${escapeHtml(tier.tierName)} Capacity Upgrade</h4>
                        <p>${escapeHtml(tier.description || 'Expand registered counter slots')}</p>
                    </div>
                    <div class="plan-price">
                        <span class="amount">$${tier.pricePerSlotUSD.toFixed(2)}</span>
                        <span class="cycle"> / additional device slot (USD)</span>
                    </div>
                    <ul class="plan-features">
                        <li><i class="fa-solid fa-check"></i> Permanent Lifetime Capacity Expansion</li>
                        <li><i class="fa-solid fa-check"></i> Expandable Up to <strong>${tier.hardCap} Max Devices</strong></li>
                        <li><i class="fa-solid fa-check"></i> Physical Drive Instance Hardware Binding</li>
                        <li><i class="fa-solid fa-check"></i> Cryptographically Signed Upgrade Key</li>
                    </ul>
                    <button class="btn-select-plan" onclick="selectUpgradeTier('${escapeHtml(tier.tierName)}')">
                        Purchase ${escapeHtml(tier.tierName)} Upgrade
                    </button>
                </div>
            `;
        }).join('');
    } catch (err) {
        console.error('Error loading hardware tiers:', err);
        grid.innerHTML = `<div class="error-msg">Error loading hardware tiers: ${err.message}</div>`;
    }
}

// 3. Plan Selection Action
function selectPlan(planId) {
    const plan = currentPlans.find(p => p.id === planId);
    if (!plan) return;

    document.getElementById('checkout-type-input').value = 'subscription';
    document.getElementById('plan-id-input').value = plan.id;
    document.getElementById('checkout-title').innerHTML = '<i class="fa-solid fa-credit-card"></i> Client Subscription Checkout';
    
    document.getElementById('device-count-group').classList.remove('hidden');
    document.getElementById('slots-add-group').classList.add('hidden');
    document.getElementById('drive-id-group').classList.remove('hidden');
    document.getElementById('drive-id-input').setAttribute('required', 'required');

    updateCheckoutPriceSummary();

    document.getElementById('checkout-section').classList.remove('hidden');
    document.getElementById('checkout-section').scrollIntoView({ behavior: 'smooth' });
}

function selectUpgradeTier(tierName) {
    const tier = hardwareTiers.find(t => t.tierName.toLowerCase() === tierName.toLowerCase());
    const unitPrice = tier ? tier.pricePerSlotUSD : (tierName === 'Cloud' ? 300 : (tierName === 'BranchBox' ? 150 : 50));

    document.getElementById('checkout-type-input').value = 'upgrade';
    document.getElementById('tier-name-input').value = tierName;
    document.getElementById('checkout-title').innerHTML = '<i class="fa-solid fa-microchip"></i> Hardware Device Capacity Upgrade Checkout';

    document.getElementById('device-count-group').classList.add('hidden');
    document.getElementById('slots-add-group').classList.remove('hidden');
    document.getElementById('drive-id-group').classList.remove('hidden');
    document.getElementById('drive-id-input').setAttribute('required', 'required');

    updateCheckoutPriceSummary();

    document.getElementById('checkout-section').classList.remove('hidden');
    document.getElementById('checkout-section').scrollIntoView({ behavior: 'smooth' });
}

function updateCheckoutPriceSummary() {
    const checkoutType = document.getElementById('checkout-type-input').value;

    if (checkoutType === 'upgrade') {
        const tierName = document.getElementById('tier-name-input').value || 'TillStick';
        const tier = hardwareTiers.find(t => t.tierName.toLowerCase() === tierName.toLowerCase());
        const unitPrice = tier ? tier.pricePerSlotUSD : (tierName === 'Cloud' ? 300.00 : (tierName === 'BranchBox' ? 150.00 : 50.00));

        let slotsToAdd = parseInt(document.getElementById('slots-add-input').value) || 1;
        if (slotsToAdd < 1) slotsToAdd = 1;

        const totalPrice = unitPrice * slotsToAdd;

        document.getElementById('selected-plan-banner').innerText = `Selected Item: Device Capacity Upgrade (${tierName} @ $${unitPrice.toFixed(2)} / slot)`;
        document.getElementById('summary-label-rate').innerText = `Tier Rate (Per Slot):`;
        document.getElementById('summary-price').innerText = `$${unitPrice.toFixed(2)} USD`;
        document.getElementById('summary-label-qty').innerText = `Slots To Add:`;
        document.getElementById('summary-devices').innerText = `+${slotsToAdd} Slot(s)`;
        document.getElementById('summary-total').innerText = `$${totalPrice.toFixed(2)} USD`;
    } else {
        const planId = document.getElementById('plan-id-input').value;
        const plan = currentPlans.find(p => p.id === planId);
        if (!plan) return;

        let deviceCount = parseInt(document.getElementById('device-count-input').value) || 1;
        if (deviceCount < 1) deviceCount = 1;

        const totalPrice = plan.price * deviceCount;

        document.getElementById('selected-plan-banner').innerText = `Selected Plan: ${plan.planName} ($${plan.price.toFixed(2)} ${plan.currency} / dev)`;
        document.getElementById('summary-label-rate').innerText = `Plan Rate (Per Device):`;
        document.getElementById('summary-price').innerText = `$${plan.price.toFixed(2)} ${plan.currency} / dev`;
        document.getElementById('summary-label-qty').innerText = `Device Count:`;
        document.getElementById('summary-devices').innerText = `${deviceCount} Device(s)`;
        document.getElementById('summary-total').innerText = `$${totalPrice.toFixed(2)} ${plan.currency}`;
    }
}

function cancelCheckout() {
    document.getElementById('checkout-section').classList.add('hidden');
}

// 4. Form Submit Listener & 2-Step TIN Verification Flow
function setupFormListeners() {
    const form = document.getElementById('checkout-form');
    form.addEventListener('submit', async (e) => {
        e.preventDefault();

        const btn = document.getElementById('btn-paynow');
        btn.disabled = true;
        btn.innerHTML = '<i class="fa-solid fa-circle-notch fa-spin"></i> Checking TIN Registration...';

        const checkoutType = document.getElementById('checkout-type-input').value;
        const tin = document.getElementById('tin-input').value.trim();
        const email = document.getElementById('email-input').value.trim();

        try {
            // Step A: Validate TIN Registration
            const valRes = await fetch('/api/v1/public/validate-tin', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ taxPayerTIN: tin })
            });

            const valData = await valRes.json();

            if (!valData.isRegistered) {
                btn.disabled = false;
                btn.innerHTML = '<i class="fa-solid fa-lock"></i> Verify Registered TIN & PayNow <i class="fa-solid fa-arrow-right"></i>';

                document.getElementById('checkout-form').classList.add('hidden');
                document.getElementById('unreg-tin-display').innerText = tin;
                document.getElementById('unregistered-lead-box').classList.remove('hidden');
                return;
            }

            // Step B: Initiate PayNow Checkout (Subscription vs Upgrade)
            btn.innerHTML = '<i class="fa-solid fa-circle-notch fa-spin"></i> Connecting to PayNow...';

            let checkoutEndpoint = '/api/v1/public/checkout/initiate';
            let payload = {};

            const driveId = document.getElementById('drive-id-input').value.trim();
            if (!driveId) {
                btn.disabled = false;
                btn.innerHTML = '<i class="fa-solid fa-lock"></i> Verify Registered TIN & PayNow <i class="fa-solid fa-arrow-right"></i>';
                alert('Drive Instance ID is required for hardware-bound subscription keys.');
                document.getElementById('drive-id-input').focus();
                return;
            }

            if (checkoutType === 'upgrade') {
                checkoutEndpoint = '/api/v1/public/checkout/initiate-upgrade';
                const tierName = document.getElementById('tier-name-input').value || 'TillStick';
                const slotsToAdd = parseInt(document.getElementById('slots-add-input').value) || 1;

                payload = {
                    taxPayerTIN: tin,
                    companyName: valData.companyName,
                    contactName: valData.contactName,
                    email: email,
                    hardwareTier: tierName,
                    slotsToAdd: slotsToAdd,
                    driveInstanceId: driveId
                };
            } else {
                const planId = document.getElementById('plan-id-input').value;
                const deviceCount = parseInt(document.getElementById('device-count-input').value) || 1;

                payload = {
                    taxPayerTIN: tin,
                    companyName: valData.companyName,
                    contactName: valData.contactName,
                    email: email,
                    planId: planId,
                    deviceCount: deviceCount,
                    driveInstanceId: driveId
                };
            }

            const checkoutRes = await fetch(checkoutEndpoint, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            const checkoutData = await checkoutRes.json();
            btn.disabled = false;
            btn.innerHTML = '<i class="fa-solid fa-lock"></i> Verify Registered TIN & PayNow <i class="fa-solid fa-arrow-right"></i>';

            if (!checkoutRes.ok) {
                alert(checkoutData.message || 'Failed to initiate PayNow checkout.');
                return;
            }

            currentTransactionId = checkoutData.transactionId;
            currentPayNowRef = checkoutData.payNowReference;

            openPayNowModal(checkoutData.planName, checkoutData.amount, checkoutData.currency, checkoutData.payNowReference);

        } catch (err) {
            btn.disabled = false;
            btn.innerHTML = '<i class="fa-solid fa-lock"></i> Verify Registered TIN & PayNow <i class="fa-solid fa-arrow-right"></i>';
            alert('Error during checkout: ' + err.message);
        }
    });

    // Unregistered Lead / Consultant Setup Form
    const leadForm = document.getElementById('consultation-form');
    leadForm.addEventListener('submit', async (e) => {
        e.preventDefault();

        const btn = document.getElementById('btn-submit-lead');
        btn.disabled = true;
        btn.innerHTML = '<i class="fa-solid fa-circle-notch fa-spin"></i> Submitting Request...';

        const payload = {
            taxPayerTIN: document.getElementById('tin-input').value.trim(),
            companyName: document.getElementById('lead-company').value.trim(),
            contactName: document.getElementById('lead-contact').value.trim(),
            email: document.getElementById('email-input').value.trim(),
            phone: document.getElementById('lead-phone').value.trim(),
            notes: document.getElementById('lead-notes').value.trim()
        };

        try {
            const res = await fetch('/api/v1/public/request-consultation', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify(payload)
            });

            const data = await res.json();
            btn.disabled = false;
            btn.innerHTML = '<i class="fa-solid fa-paper-plane"></i> Request Consultant Setup';

            document.getElementById('unregistered-lead-box').classList.add('hidden');
            document.getElementById('lead-success-msg').innerText = data.message;
            document.getElementById('consultation-success-box').classList.remove('hidden');
        } catch (err) {
            btn.disabled = false;
            btn.innerHTML = '<i class="fa-solid fa-paper-plane"></i> Request Consultant Setup';
            alert('Failed to submit consultation request: ' + err.message);
        }
    });
}

function resetFormToTin() {
    document.getElementById('unregistered-lead-box').classList.add('hidden');
    document.getElementById('checkout-form').classList.remove('hidden');
}

// 5. PayNow Sandbox Simulator Handling
function openPayNowModal(planName, amount, currency, ref) {
    document.getElementById('sandbox-plan-name').innerText = planName;
    document.getElementById('sandbox-amount').innerText = `$${amount.toFixed(2)} ${currency}`;
    document.getElementById('sandbox-ref').innerText = ref;
    document.getElementById('paynow-modal').classList.remove('hidden');
}

function closePayNowModal() {
    document.getElementById('paynow-modal').classList.add('hidden');
}

async function approveSimulatedPayment() {
    if (!currentTransactionId) return;

    const btn = document.getElementById('btn-approve-sim');
    btn.disabled = true;
    btn.innerHTML = '<i class="fa-solid fa-circle-notch fa-spin"></i> Authorizing Payment...';

    try {
        const res = await fetch('/api/v1/public/checkout/simulate-paynow', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ transactionId: currentTransactionId })
        });

        const data = await res.json();
        btn.disabled = false;
        btn.innerHTML = '<i class="fa-solid fa-circle-check"></i> Authorize & Complete Payment';

        if (!res.ok || !data.success) {
            alert(data.message || 'Payment simulation failed.');
            return;
        }

        closePayNowModal();
        showConfirmation(data);
    } catch (err) {
        btn.disabled = false;
        btn.innerHTML = '<i class="fa-solid fa-circle-check"></i> Authorize & Complete Payment';
        alert('Error processing simulated payment: ' + err.message);
    }
}

// 6. Confirmation View Render
function showConfirmation(data) {
    document.getElementById('checkout-section').classList.add('hidden');
    document.getElementById('confirm-email').innerText = data.clientEmail;
    document.getElementById('api-key-display').value = data.apiKey;
    document.getElementById('confirm-tin').innerText = document.getElementById('tin-input').value;
    document.getElementById('confirm-plan').innerText = data.planName;
    document.getElementById('confirm-validto').innerText = new Date(data.validTo).toLocaleDateString('en-US', { year: 'numeric', month: 'long', day: 'numeric' });

    document.getElementById('confirmation-section').classList.remove('hidden');
    document.getElementById('confirmation-section').scrollIntoView({ behavior: 'smooth' });
}

function copyApiKey() {
    const input = document.getElementById('api-key-display');
    input.select();
    document.execCommand('copy');
    alert('API Key copied to clipboard!');
}

function resetPortal() {
    document.getElementById('confirmation-section').classList.add('hidden');
    document.getElementById('checkout-form').reset();
    window.scrollTo({ top: 0, behavior: 'smooth' });
}

function escapeHtml(str) {
    return (str || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
}
