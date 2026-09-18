#!/bin/sh
set -eu

# Runtime configuration, so one image runs unchanged in Docker Compose and in Azure Container Apps.
#
#   API_BASE_URL  what the browser uses for API calls. Empty = same origin (nginx proxies /api).
#   API_UPSTREAM  where nginx forwards /api, /hubs and /graphql. "api:8080" under Compose;
#                 the API container app's internal name in Azure.
#
# The DNS resolver is read from /etc/resolv.conf rather than hard-coded: Docker's embedded DNS is
# 127.0.0.11, Container Apps uses something else, and nginx needs whichever is actually present to
# re-resolve the upstream when the API is restarted or scaled.

API_UPSTREAM="${API_UPSTREAM:-http://api:8080}"
RESOLVER="$(awk '/^nameserver/ { print $2; exit }' /etc/resolv.conf)"
RESOLVER="${RESOLVER:-127.0.0.11}"

sed -i \
    -e "s|__API_UPSTREAM__|${API_UPSTREAM}|g" \
    -e "s|__RESOLVER__|${RESOLVER}|g" \
    /etc/nginx/nginx.conf

cat > /usr/share/nginx/html/config.js <<CONFIG
window.__APP_CONFIG__ = {
  apiBaseUrl: "${API_BASE_URL:-}"
};
CONFIG

exec nginx -g 'daemon off;'
