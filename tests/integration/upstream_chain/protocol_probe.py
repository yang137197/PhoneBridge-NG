import base64, hashlib, http.client, json, ssl, time
from start_android import auth_password, RUN
from zeroconf import Zeroconf, ServiceBrowser

auth = 'Basic ' + base64.b64encode(('phonebridge:' + auth_password()).encode()).decode()
def connection():
    return http.client.HTTPSConnection('127.0.0.1', 18273, context=ssl._create_unverified_context(), timeout=10)

results = {}
conn = connection()
conn.request('GET', '/phone-origin-100MB.bin', headers={'Authorization': auth, 'Range': 'bytes=67108864-67109887'})
response = conn.getresponse()
sample = response.read(1024)
results['range'] = {'request': 'bytes=67108864-67109887', 'status': response.status, 'content_length': response.getheader('Content-Length'), 'content_range': response.getheader('Content-Range'), 'returned_sample_sha256': hashlib.sha256(sample).hexdigest()}
conn.close()

conn = connection()
body = b'<?xml version="1.0"?><d:propfind xmlns:d="DAV:"><d:allprop/></d:propfind>'
conn.request('PROPFIND', '/', body, {'Authorization': auth, 'Depth': '1', 'Content-Type': 'application/xml'})
response = conn.getresponse()
response.read()
first = response.status
conn.request('OPTIONS', '/', headers={'Authorization': auth})
response = conn.getresponse()
error = response.read().decode(errors='replace')
results['keepalive_body'] = {'propfind_status': first, 'next_options_status': response.status, 'next_response': error[:250]}
conn.close()
print(json.dumps(results), flush=True)

seen = []
class Listener:
    def add_service(self, zc, kind, name):
        info = zc.get_service_info(kind, name)
        if info:
            seen.append({'name': name, 'addresses': info.parsed_addresses(), 'port': info.port})
    def update_service(self, zc, kind, name):
        pass
    def remove_service(self, zc, kind, name):
        pass

with Zeroconf() as zc:
    browser = ServiceBrowser(zc, '_phonebridge._tcp.local.', Listener())
    time.sleep(30)
    browser.cancel()
results['mdns'] = {'duration_seconds': 30, 'phonebridge_services': seen, 'scope': 'Windows host observing isolated emulator NAT'}
(RUN / 'protocol-results.json').write_text(json.dumps(results, indent=2), encoding='utf-8')
print(json.dumps(results['mdns']), flush=True)
