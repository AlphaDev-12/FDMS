import urllib.request
import json

data = {
    'tenantApiKey': 'css_tenant_prod_76b96dd9fb5e',
    'email': 'vermarxy@gmail.com',
    'fullName': 'Valid Makuyana',
    'password': 'password123'
}

req = urllib.request.Request(
    'http://localhost:5045/api/v1/tenant/auth/register',
    data=json.dumps(data).encode(),
    headers={'Content-Type': 'application/json'}
)

try:
    res = urllib.request.urlopen(req)
    print('STATUS:', res.status)
    print(res.read().decode('utf-8'))
except urllib.error.HTTPError as e:
    print('HTTP STATUS:', e.code)
    print(e.read().decode('utf-8'))
