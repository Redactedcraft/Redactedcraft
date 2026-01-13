$path = "C:\Users\Redacted\Documents\redactedcraft.github.io\index.html"
$content = Get-Content $path -Raw

# Replace longer sequences first to avoid partial matches
$content = $content.Replace("ðŸ”§", "🔧") # Wrench
$content = $content.Replace("ðŸ›¡ï¸", "🛡️") # Shield
$content = $content.Replace("ðŸ› ï¸", "🛠️") # Hammer/Wrench
$content = $content.Replace("âš–ï¸", "⚖️") # Scales
$content = $content.Replace("â™»ï¸", "♻️") # Recycle
$content = $content.Replace("ðŸ–¼ï¸", "🖼️") # Frame
$content = $content.Replace("ðŸ—ï¸", "🗺️") # Map

# Single/Short sequences
$content = $content.Replace("âš¡", "⚡") # Bolt
$content = $content.Replace("ðŸ“¡", "📡") # Satellite
$content = $content.Replace("ðŸš€", "🚀") # Rocket
$content = $content.Replace("ðŸ ", "🏠") # House
$content = $content.Replace("ðŸ“œ", "📜") # Scroll
$content = $content.Replace("ðŸ§¹", "🧹") # Broom
$content = $content.Replace("ðŸŽ¨", "🎨") # Palette
$content = $content.Replace("ðŸ“¦", "📦") # Box
$content = $content.Replace("ðŸªŸ", "🪟") # Window
$content = $content.Replace("ðŸ”", "🔍") # Mag Glass (Generic search icon fallback)

Set-Content -Path $path -Value $content -Encoding UTF8
Write-Host "Fixed Mojibake in index.html"
