# syntax=docker/dockerfile:1
# Placeholder service image. Any language works as long as the container
# listens on deployment.port and answers deployment.health_path.
FROM node:22-alpine
WORKDIR /app
COPY server.mjs ./
ENV PORT=8080 NODE_ENV=production
EXPOSE 8080
# Run as the unprivileged user that ships with the base image.
USER node
CMD ["node", "server.mjs"]
