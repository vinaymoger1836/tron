FROM node:22-alpine
WORKDIR /app
COPY package.json lan-server.mjs online-server.mjs ./
COPY dist ./dist
ENV PORT=8000
EXPOSE 8000
USER node
CMD ["node", "online-server.mjs"]
