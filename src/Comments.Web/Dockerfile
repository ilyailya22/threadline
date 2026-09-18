# syntax=docker/dockerfile:1
#
# Build context is the repository root (see docker-compose.yml) so that all three Dockerfiles use
# the same context and Docker can share the build cache between them.

FROM node:24-alpine AS build
WORKDIR /app

# package-lock.json first: this layer is cached until a dependency actually changes, which is what
# keeps a source-only rebuild from reinstalling node_modules every time.
COPY src/Comments.Web/package.json src/Comments.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund

COPY src/Comments.Web/ ./
RUN npm run build -- --configuration production

FROM nginx:1.27-alpine AS final

COPY src/Comments.Web/nginx.conf /etc/nginx/nginx.conf
COPY --from=build /app/dist/Comments.Web/browser /usr/share/nginx/html
COPY src/Comments.Web/docker-entrypoint.sh /docker-entrypoint.d/../entrypoint.sh

RUN chmod +x /entrypoint.sh

ENV API_BASE_URL=""

EXPOSE 8080

HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=5 \
    CMD wget --quiet --spider http://127.0.0.1:8080/index.html || exit 1

ENTRYPOINT ["/entrypoint.sh"]
