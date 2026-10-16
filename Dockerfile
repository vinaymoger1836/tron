FROM node:22-alpine
WORKDIR /app
COPY package.json lan-server.mjs online-server.mjs ./
COPY dist ./dist
ENV PORT=10000
EXPOSE 10000
USER node
CMD ["node", "online-server.mjs"]
