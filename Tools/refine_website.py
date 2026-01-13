import os

path = r"C:\Users\Redacted\Documents\redactedcraft.github.io\index.html"

with open(path, 'rb') as f:
    data = f.read()

# 1. Refine VR Detection Logic
# Current: const isQuest = /OculusBrowser|Quest/i.test(navigator.userAgent);
# Change to: allow manual override via URL param ?debugvr=1 or show if Quest
old_js = b"const isQuest = /OculusBrowser|Quest/i.test(navigator.userAgent);"
new_js = b"const isQuest = /OculusBrowser|Quest/i.test(navigator.userAgent) || window.location.search.includes('debugvr=1');"

if old_js in data:
    data = data.replace(old_js, new_js)

# 2. Add Devlog Entry for Mobile 3D Fix (if not already there)
log_marker = b'<div id="log-website" class="sub-content">'
new_log = """
                <div class="log-entry">
                    <div class="log-header">
                        <h3>📱 UX: Mobile 3D Model Support</h3>
                        <span class="log-date">JAN 12, 2026</span>
                    </div>
                    <div class="log-body">
                        <p>Optimized the 3D block inspector for touch devices. Mobile users can now rotate and inspect block models using intuitive touch-and-drag gestures, bringing full feature parity between desktop and mobile browsers.</p>
                    </div>
                </div>"""

if b'Mobile 3D Model Support' not in data:
    data = data.replace(log_marker, log_marker + b'\n' + new_log)

with open(path, 'wb') as f:
    f.write(data)

print("Updated index.html detection logic and verified devlog.")
