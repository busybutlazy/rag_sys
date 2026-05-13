FROM node:20-alpine AS builder
WORKDIR /app
COPY package.json package-lock.json* ./
RUN npm ci
COPY . .
ARG VITE_BE_API_URL=/api
ARG VITE_AI_API_URL=/ai
ENV VITE_BE_API_URL=${VITE_BE_API_URL}
ENV VITE_AI_API_URL=${VITE_AI_API_URL}
RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY --from=builder /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
CMD ["nginx", "-g", "daemon off;"]
