# Docker Manifest Troubleshooting

By the end of this exercise, you should be able to:

 - Learn the details of a good written YAML file


## Introduction

You may often have to write your own YAML files, copy YAMLs from the internet or receive YAMLs from a customer. YAML files are bound to rules, which you must uphold to make a manifest deployable. You will find two examples, one for Docker and one for Kubernetes. Try to make those manifests running.

## Docker Stack/Compose file

```
version: 3.2

services:
  reverse_proxy:
    image: 
    dockersamples/atseasampleshopapp_reverse_proxy
    ports:
      - "80:80"
      - "443:443"
    secrets:
      - source: revprox_cert
        target: revprox_cert
      - source: revprox_key
        target: revprox_key
    networks:
      - front-tier

  database:
    image: dockersamples/atsea_db
    environment:
      POSTGRES_USER: gordonuser
      POSTGRES_DB_PASSWORD_FILE: /run/secrets/postgres_password
      POSTGRES_DB: atsea
    networks:
      - back-tier
    secrets:
      - postgres_password
    deploy:
      placement:
        constraints:
          - node.role == worker

  appserver:
    image: dockersamples/atsea_app
    networks:
      front-tier
      back-tier
      payment
    deploy:
      replicas: 2
      update_config:
        parallelism: 2
        failure_action: rollback
      placement:
        constraints:
          - 'node.role == worker'
      restart_policy:
        condition: on-failure
        delay: 5s
        max_attempts: 3
        window: 120s
    secrets:
      - postgres_password

  visualizer:
    image: dockersamples/visualizer:stable
    ports:
      - "8001:8080"
    stop_grace_period: 1m30s
    volumes:
      - "/var/run/docker.sock:/var/run/docker.sock"
    deploy:
     update_config:
        failure_action: rollback
      placement:
        constraints:
          - 'node.role == manager'

  payment_gateway:
    image: dockersamples/atseasampleshopapp_payment_gateway
    secrets:
      - source: staging_token
        target: payment_token
    networks:
      - payment
    deploy:
      update_config:
        failure_action: rollback
      placement:
        constraints:
           - 'node.role == worker'
          - 'node.labels.pcidss == yes'

networks:
  front-tier:
  back-tier
  payment:
    driver: overlay
    driver_opts:
      encrypted: 'yes'

secrets:
  postgres_password:
    external: true
  staging_token:
    external: true
  revprox_key:
    external: true
  revprox_cert:
    external: true
```

## Kubernetes Deployment
```
apiVersion: v1
kind: 
Service
metadata:
  name: db
  labels:
    app: words-db
spec:
  ports:
    - port: 5432
      targetPort: 5432
      name: db
  selector:
    app: words-db
  clusterIP: None
---
apiVersion: apps/v1beta1
kind: Deployment
metadata:
  name: db
  labels:
    app: words-db
spec:
  template:
    metadata:
      labels:
      app: words-db
    spec:
      containers:
      - name: db
        image: dockersamples/k8s-wordsmith-db
        ports:
        - containerPort: 5432
          name: db
---
apiVersion: v1
kind: Service
metadata:
  - name: words
  - labels:
      app: words-api
spec:
  ports:
    - port: 8080
      targetPort: 8080
      name: api
  selector:
    app: words-api
  clusterIP: None
---
apiVersion: apps/v1beta1
kind: Deployment
metadata:
  name: words
  labels:
    app: words-api
spec:
  replicas: 5
  template:
    metadata:
      labels:
        app: words-api
    spec:
      containers:
      - name: words
        image: dockersamples/k8s-wordsmith-api
        ports:
        - containerPort: 8080
        - name: api
---
apiVersion: v1
kind: Service
metadata:
  name: web
  labels:
    app: words-web
spec:
  ports:
    - port: 8081
      targetPort: 80
      name: web
  selector:
    app: words-web
  type: LoadBalancer

apiVersion: apps/v1beta1
kind: Deployment
metadata:
  name: web
  labels:
    app: words-web
spec:
  template:
    metadata:
      labels:
        app: words-web
    spec:
    containers:
      - name: web
        image: dockersamples/k8s-wordsmith-web
        ports:
        - containerPort: 80
          name: words-web
```

## Conclusion

Make sure you will use all available tools suche as `Visual Studio Code` or others to find errors in YAML files to save you valuable time.

**Further reading:**

https://github.com/dockersamples/atsea-sample-shop-app/blob/master/docker-stack.yml
https://github.com/dockersamples/k8s-wordsmith-demo/blob/master/kube-deployment.yml


