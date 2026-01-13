import os

path = r"C:\Users\Redacted\Documents\redactedcraft.github.io\index.html"

with open(path, 'rb') as f:
    data = f.read()

# 1. Binary replacements
replacements = [
    (b'\xf0\x9f\x94\x8d\xc2\xa7', "🔧".encode('utf-8')),
    (b'\xc3\xb0\xc5\xb8\xc2\x9b\xc2\xa0\xc3\xaf\xc2\xb8\xc2\x8f', "🛠️".encode('utf-8')),
    (b'\xc3\xb0\xc5\xb8\xc2\x8f\xe2\x80\x94\xc3\xaf\xc2\xb8\xc2\x8f', "🗺️".encode('utf-8')),
    (b'\xc2\x8f', b''),
    (b'\xc2\xad', b''),
    (b'\xef\xbf\xbd', b''),
]

for old, new in replacements:
    data = data.replace(old, new)

# 2. Add New Devlog Entry
marker = b'<div id="log-website" class="sub-content">'
new_log_str = """
                <div class="log-entry">
                    <div class="log-header">
                        <h3>📱 UX: Mobile 3D Model Support</h3>
                        <span class="log-date">JAN 12, 2026</span>
                    </div>
                    <div class="log-body">
                        <p>Optimized the 3D block inspector for touch devices. Mobile users can now rotate and inspect block models using intuitive touch-and-drag gestures, bringing full feature parity between desktop and mobile browsers.</p>
                    </div>
                </div>"""
new_log = new_log_str.encode('utf-8')

if b'Mobile 3D Model Support' not in data:
    data = data.replace(marker, marker + b'\n' + new_log)

with open(path, 'wb') as f:
    f.write(data)

print("Scrubbed artifacts and added Mobile 3D fix devlog.")
