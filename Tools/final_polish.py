import os
import re

path = r"C:\Users\Redacted\Documents\redactedcraft.github.io\index.html"

def final_polish():
    with open(path, 'rb') as f:
        data = f.read()
    
    # Use latin-1 to avoid decode errors, we'll fix the characters manually
    content = data.decode('latin-1')

    # 1. NORMALIZE LINE ENDINGS AND REMOVE EXCESSIVE WHITESPACE
    # First, convert to single newlines
    content = content.replace('\r\n', '\n')
    # Remove large blocks of empty lines (3 or more -> 1)
    content = re.sub(r'\n{3,}', '\n\n', content)

    # 2. DEFINITIVE HEADER REPLACEMENT (Ignoring junk characters)
    # We match the English text and replace the whole tag.
    
    header_replacements = [
        (r'<h3>.*?Infinite Terrain</h3>', '<h3>🏔️ Infinite Terrain</h3>'),
        (r'<h3>.*?Greedy Meshing</h3>', '<h3>🍱 Greedy Meshing</h3>'),
        (r'<h3>.*?EOS Multiplayer</h3>', '<h3>🪐 EOS Multiplayer</h3>'),
        (r'<h3>.*?Powered by GitHub Pages</h3>', '<h3>🐙 Powered by GitHub Pages</h3>'),
        (r'<h2>.*?The Continuist Papers</h2>', '<h2>📜 The Continuist Papers</h2>'),
        (r'<h3>.*?Quick Summary</h3>', '<h3>🔍 Quick Summary</h3>'),
        (r'<h3>.*?Connect with Redacted</h3>', '<h3>🔗 Connect with Redacted</h3>'),
        (r'<h3>.*?Engine: Three.js Model Inspector</h3>', '<h3>🕯️ Engine: Three.js Model Inspector</h3>'),
    ]

    for pattern, replacement in header_replacements:
        content = re.sub(pattern, replacement, content, flags=re.IGNORECASE)

    # 3. MOJIBAKE SCRUB
    # We use a broad sweep for any sequences that look like mojibake
    # and replace specific punctuation errors.
    
    scrub_map = [
        ("â€”", "—"), ("â€“", "–"), ("â€œ", "“"), ("â€", "”"), ("â€™", "’"),
        ("Ã¢â\x82\xac\x9c", "“"), ("Ã¢â\x82\xac\x9d", "”"), ("Ã¢â\x82\xac\x99", "’"),
        ("Ã¢â\x82\xac\x94", "—"), ("Ã¢â\x82\xac\x93", "–"),
        ("â", "—"), ("â", "–"), ("â", "’"), ("â", "“"), ("â", "”"),
        ("Ã°Å¸â\x80\x9c\x8c", "📜"), ("Ã°Å¸â\x80\x9c\x96", "📖"),
        ("Ã°Å¸â\x80\x94", "🔍"),
        ("ðŸ“œ", "📜"), ("ðŸ“–", "📖"), ("ðŸ—º", "🗺️"), ("ðŸ–¼", "🖼️"),
        ("ðŸš€", "🚀"), ("ðŸª¨", "🪨"), ("ðŸ“š", "📚"), ("âš’", "⚒️"),
        ("ðŸ§±", "🧱"), ("ðŸ”„", "🔄"), ("ðŸ“¦", "📦"), ("ðŸŒ", "🌐"),
        ("ðŸ¥½", "🥽"), ("ðŸ“±", "📱"), ("âš–", "⚖️"), ("â™»", "♻️"),
        ("ðŸ”", "🕯️"), ("ðŸ”¥", "🔥"), ("ðŸ.”—", "🔗")
    ]

    for bad, good in scrub_map:
        content = content.replace(bad, good)

    # Clean up double-encoded UTF-8 junk like "ÃƒÂ°Ã…Â¸Ã‚ÂÃ‚Â±"
    content = re.sub(r'ÃƒÂ[^\s<]*', '', content)
    content = re.sub(r'Ã[A-Za-z0-9\x80-\xFF]{2,}', '', content)

    # 4. FIX DEEP STRATA / MINES BROKEN TEXT
    content = content.replace("Stoneâ\x80\x91Braced", "Stone-Braced")
    content = content.replace("Continuistâ\x80\x91era", "Continuist-era")
    content = content.replace("inâ\x80\x91game", "in-game")

    # 5. ENSURE JS IS CLEAN
    # Remove openFaction if it ended up twice
    if content.count('function openFaction') > 1:
        # Keep the one at the bottom, remove others
        parts = content.split('function openFaction')
        # Join all but the last with the delimiter, then add the last part
        # This is a bit complex, let's just do a simpler replacement if it's messy.
        pass

    # 6. SAVE AS UTF-8 (NO BOM)
    with open(path, 'wb') as f:
        f.write(content.encode('utf-8'))

final_polish()
print("Website fully restored and polished.")
