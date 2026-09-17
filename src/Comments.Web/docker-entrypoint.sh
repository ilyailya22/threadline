#!/bin/sh
set -eu

# Writes the runtime configuration the application reads at startup.
#
# The alternative — baking the API URL into the bundle at build time — means one image per
# environment and a rebuild to change a URL. Generating a tiny config.js here lets the same image
# run locally, in staging and in production.
#
# API_BASE_URL is empty by default, which means "same origin": nginx proxies /api and /hubs to the
# API container, so the browser never makes a cross-origin request.

cat > /usr/share/nginx/html/config.js <<EOF
window.__APP_CONFIG__ = {
  apiBaseUrl: "${API_BASE_URL:-}"
};
EOF

exec nginx -g 'daemon off;'
