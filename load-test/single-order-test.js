import http from 'k6/http';
import { check } from 'k6';
import {
    buildAddToCartFormData,
    buildAjaxHeaders,
    buildBillingAddressFormData,
    buildCheckoutAttributeFormData,
    buildHeaders,
    buildPaymentMethodFormData,
    buildShippingMethodFormData,
    encodeFormData,
    extractAntiForgeryToken,
    extractCookies,
    extractOrderId,
    logCheckoutProgress,
    mergeCookies,
    validateCheckoutStepResponse,
} from './lib/nopcommerce-helpers.js';

// Places exactly ONE guest-checkout order and reports the resulting order id.
//
// This is intentionally minimal and deterministic (one product, one payment method) so it can be
// driven by single-order-rejected.sh to exercise the WMS contradiction → fulfillment Rejected path.
// k6 only proves the checkout itself; the async Rejected outcome is asserted DB-side by the wrapper.

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8080';

// Deterministic choices for a repeatable single order.
const PRODUCT_ID = 3;
const PRODUCT_URL = '/lenovo-ideacentre';
// Check / Money Order needs no card details → simplest payment path.
const PAYMENT_METHOD = 'Payments.CheckMoneyOrder';
const SHIPPING_OPTION = 'Ground___Shipping.FixedByWeightByTotal';

export const options = {
    scenarios: {
        single_order: {
            executor: 'per-vu-iterations',
            vus: 1,
            iterations: 1,
            maxDuration: __ENV.MAX_DURATION || '2m',
        },
    },
    // Every check must pass for the run to be considered successful (drives k6's exit code).
    thresholds: {
        checks: ['rate>0.99'],
    },
};

export default function () {
    const vu = __VU;
    const iter = __ITER;
    let cookies = {};

    logCheckoutProgress(vu, 'START', 'Placing a single order', { productId: PRODUCT_ID });

    let response = http.get(`${BASE_URL}/`, { headers: buildHeaders(), tags: { name: 'GET /' } });
    if (!check(response, { 'Homepage loaded': (r) => r.status === 200 })) {
        return fail(vu, 'Homepage', response);
    }
    cookies = mergeCookies(cookies, extractCookies(response));

    response = http.get(`${BASE_URL}${PRODUCT_URL}`, {
        headers: buildHeaders(cookies),
        tags: { name: `GET ${PRODUCT_URL}` },
    });
    if (!check(response, { 'Product page loaded': (r) => r.status === 200 })) {
        return fail(vu, 'Product Page', response);
    }
    cookies = mergeCookies(cookies, extractCookies(response));

    let token = extractAntiForgeryToken(response);
    if (!check(token, { 'Anti-forgery token found': (t) => !!t })) {
        return fail(vu, 'Product Page', response, 'no anti-forgery token');
    }

    response = http.post(
        `${BASE_URL}/addproducttocart/details/${PRODUCT_ID}/1`,
        encodeFormData(buildAddToCartFormData(PRODUCT_ID, 1, token)),
        { headers: buildAjaxHeaders(cookies, token), tags: { name: 'POST /addproducttocart' } }
    );
    if (!check(response, {
        'Product added to cart': (r) => {
            if (r.status !== 200) return false;
            try {
                const body = typeof r.body === 'string' ? JSON.parse(r.body) : r.body;
                return body.success === true || !!body.message;
            } catch (e) {
                return r.status === 200;
            }
        },
    })) {
        return fail(vu, 'Add to Cart', response);
    }
    cookies = mergeCookies(cookies, extractCookies(response));

    response = http.post(
        `${BASE_URL}/shoppingcart/CheckoutAttributeChange?isEditable=true`,
        encodeFormData(buildCheckoutAttributeFormData(token, 1, 1)),
        { headers: buildAjaxHeaders(cookies, token), tags: { name: 'POST /CheckoutAttributeChange' } }
    );
    if (!check(response, { 'Checkout attributes saved': (r) => r.status === 200 })) {
        return fail(vu, 'Checkout Attributes', response);
    }
    cookies = mergeCookies(cookies, extractCookies(response));

    response = http.get(`${BASE_URL}/onepagecheckout`, {
        headers: buildHeaders(cookies),
        tags: { name: 'GET /onepagecheckout' },
    });
    if (!check(response, {
        'Checkout page loaded': (r) => {
            if (r.status !== 200 && r.status !== 302) return false;
            const url = r.url || '';
            if (url.includes('/cart')) return false;
            return url.includes('/onepagecheckout')
                || (typeof r.body === 'string' && r.body.toLowerCase().includes('checkout-billing-load'));
        },
    })) {
        return fail(vu, 'Checkout Page', response, 'guest checkout may be disabled');
    }
    cookies = mergeCookies(cookies, extractCookies(response));
    token = extractAntiForgeryToken(response) || token;

    response = http.post(
        `${BASE_URL}/checkout/OpcSaveBilling`,
        encodeFormData(buildBillingAddressFormData(token, vu, iter)),
        { headers: buildAjaxHeaders(cookies, token), tags: { name: 'POST /OpcSaveBilling' } }
    );
    if (!check(response, { 'Billing saved': () => validateCheckoutStepResponse(response, 'Billing') })) {
        return fail(vu, 'Billing', response);
    }
    cookies = mergeCookies(cookies, extractCookies(response));

    response = http.post(
        `${BASE_URL}/checkout/OpcSaveShippingMethod`,
        encodeFormData(buildShippingMethodFormData(token, SHIPPING_OPTION)),
        { headers: buildAjaxHeaders(cookies, token), tags: { name: 'POST /OpcSaveShippingMethod' } }
    );
    if (!check(response, { 'Shipping method saved': () => validateCheckoutStepResponse(response, 'Shipping') })) {
        return fail(vu, 'Shipping Method', response);
    }
    cookies = mergeCookies(cookies, extractCookies(response));

    response = http.post(
        `${BASE_URL}/checkout/OpcSavePaymentMethod`,
        encodeFormData(buildPaymentMethodFormData(token, PAYMENT_METHOD)),
        { headers: buildAjaxHeaders(cookies, token), tags: { name: 'POST /OpcSavePaymentMethod' } }
    );
    if (!check(response, { 'Payment method saved': () => validateCheckoutStepResponse(response, 'Payment Method') })) {
        return fail(vu, 'Payment Method', response);
    }
    cookies = mergeCookies(cookies, extractCookies(response));

    // Check/Money-Order carries no card info — just the verification token.
    response = http.post(
        `${BASE_URL}/checkout/OpcSavePaymentInfo`,
        encodeFormData({ __RequestVerificationToken: token }),
        { headers: buildAjaxHeaders(cookies, token), tags: { name: 'POST /OpcSavePaymentInfo' } }
    );
    if (!check(response, { 'Payment info saved': () => validateCheckoutStepResponse(response, 'Payment Info') })) {
        return fail(vu, 'Payment Info', response);
    }
    cookies = mergeCookies(cookies, extractCookies(response));

    response = http.post(
        `${BASE_URL}/checkout/OpcConfirmOrder`,
        encodeFormData({ __RequestVerificationToken: token, checkout_attribute_1: '1' }),
        {
            headers: buildAjaxHeaders(cookies, token),
            tags: { name: 'POST /OpcConfirmOrder', critical: 'true' },
            timeout: '30s',
        }
    );

    const placed = check(response, {
        'Order placement successful': (r) => {
            if (r.status !== 200) return false;
            try {
                const body = typeof r.body === 'string' ? JSON.parse(r.body) : r.body;
                return body.success === 1 || body.success === true;
            } catch (e) {
                return false;
            }
        },
    });

    if (!placed) {
        return fail(vu, 'Order Placement', response);
    }

    // nopCommerce's OPC confirm redirects to /checkout/completed/ without the id in the URL,
    // so fall back to reading the order number from the completed page's order-details link.
    let orderId = extractOrderId(response);
    if (!orderId) {
        const completed = http.get(`${BASE_URL}/checkout/completed/`, {
            headers: buildHeaders(cookies),
            tags: { name: 'GET /checkout/completed' },
        });
        const match = typeof completed.body === 'string' ? completed.body.match(/orderdetails\/(\d+)/i) : null;
        if (match) orderId = parseInt(match[1], 10);
    }

    logCheckoutProgress(vu, 'ORDER PLACED', 'SUCCESS', { orderId: orderId || 'unknown' });
    // Best-effort machine-parseable line; single-order-rejected.sh also resolves the order from the DB.
    console.log(`SINGLE_ORDER_RESULT order_id=${orderId || ''}`);
}

function fail(vu, step, response, note) {
    const details = { status: response && response.status };
    if (note) details.note = note;
    if (response && response.body) details.body = String(response.body).substring(0, 200);
    logCheckoutProgress(vu, step, 'FAIL', details);
    console.log('SINGLE_ORDER_RESULT order_id=');
}

export function setup() {
    console.log('========================================');
    console.log(' Single-order test (nopCommerce)');
    console.log(`  Target : ${BASE_URL}`);
    console.log(`  Product: ${PRODUCT_ID} (${PRODUCT_URL})`);
    console.log(`  Payment: ${PAYMENT_METHOD.replace('Payments.', '')}`);
    console.log('  Places exactly one guest-checkout order.');
    console.log('========================================');
}
