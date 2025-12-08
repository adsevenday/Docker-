FROM node:18-alpine
EXPOSE 8888
WORKDIR /app
COPY package*.json .
RUN npm ci --omit=dev
COPY . .
ENTRYPOINT [ "npm", "run", "start" ]