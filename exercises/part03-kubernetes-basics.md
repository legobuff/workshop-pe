# Getting started with Kubernetes

By the end of this exercise, you should be able to:

 - Implement a basic translation from Docker to Kubernetes Orchestration
 - Get to know Pods, Services, Persistent Storage, NodePorts and Ingress
 - Deploy your first Kubernetes microservice application


## Introduction

This exercise will provide you with a quick introduction to Kubernetes services, including some extras. You will be able to access a service via DNS and save database data. The app we are going to deploy will be a simple wordpress instance with a mysql database attached to it. Wordpress and mysql will run in their own pods.

Before you begin this exercise you should:
- Have a UCP/DTR installation in place
- Know how to use and where to find a UCP Client Bundle


## Part 1 - Kubernetes for Docker users

Let's translate a couple of Kubernetes terms into the Docker language:

### Pods
- Kubernetes schedules pods as its fundamental unit of work. Pods can consist of one or many containers. One important to know about pods is that containers within the same pod can communicate via `127.0.0.1 ` with each other. For example, a webserver can contact a MySQL DB listening on port 3306 at 127.0.0.1:3306 if they are both containers running in the same pod.
- `kubectl` is the preferred tool for managing pods and the containers they contain from the command line. It's also possible to manipulate the containers in a pod with the Docker CLI, though this is usually best avoided; `kubectl` should provide complete and consistent functionality for managing your pods.
- Have a look here for further translation help: https://kubernetes.io/docs/reference/kubectl/docker-cli-to-kubectl/

### Deployments
- Deployments schedule and manage groups of identically configured pods across your Kubernetes cluster. In addition to allowing you to declare all these pods simultaneously, Deployments ensure these pods get rescheduled if any of them exit, and provide facilities for performing controlled upgrades and reconfigurations to your pods.
- Deployments are actually only one example of Kuberentes *controllers*, which are objects proactively maintained in a consistent, desired state by the Kube master's controller manager.

### Services
- Services in Kubernetes are used to make your pod available to other services in and outside of your Kubernetes cluster. Services can be Ingress, NodePort, and ClusterIP among others.
- We will take a look into services in this exercise; you can read all details in full here: https://kubernetes.io/docs/concepts/services-networking/service/

### Ingress
- An Ingress is a networking abstraction for routing traffic into your cluster from an external network. Ingresses and their behavior are managed by IngressController objects such as NGINX or Traefik. 
- Other third party ingress controllers, including hardware based ones such as f5, Citrix NetScaler may be used as well.

### Persistant Volume (PV) & Persistant Volume Claim (PVC)
- Kubernetes uses PVs and PVC in combination to provide volumes to pods. You could think of PVs as the defined storage area while PVC will be the mount for the pod.  
- Usually you define a PV first (which can be any kind of storage, NFS, Cloud, etc) and then "mount" this storage with a PVC.

### Namespace
- Think of a namespace as of a playground for all your kubernetes objects, such as pods, services, etc. 
- You can split your cluster into multiple namespaces.
- You can even add management rights to each namespace.


## Part 2 - Preparing Kubernetes yaml

Kubernetes uses yaml manifests to describe all objects it orchestrates, similar to stack files in Docker Swarm. The content of the files differ though. Let's have a look into our yaml:

```
apiVersion: v1
kind: Service
metadata:
  name: wordpress-mysql
  labels:
    app: wordpress
spec:
  ports:
    - port: 3306
  selector:
    app: wordpress
    tier: mysql
  clusterIP: None
---
kind: PersistentVolume
apiVersion: v1
metadata:
  name: mysql-pv-volume
  labels:
    type: local
spec:
  storageClassName: manual
  capacity:
    storage: 20Gi
  accessModes:
    - ReadWriteOnce
  hostPath:
    path: "/mnt/data"
---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: mysql-pv-claim
  labels:
    app: wordpress
spec:
  storageClassName: manual
  accessModes:
    - ReadWriteOnce
  resources:
    requests:
      storage: 2Gi
---
apiVersion: apps/v1 # for versions before 1.9.0 use apps/v1beta2
kind: Deployment
metadata:
  name: wordpress-mysql
  labels:
    app: wordpress
spec:
  selector:
    matchLabels:
      app: wordpress
      tier: mysql
  strategy:
    type: Recreate
  template:
    metadata:
      labels:
        app: wordpress
        tier: mysql
    spec:
      containers:
      - image: mysql:5.6
        name: mysql
        env:
        - name: MYSQL_ROOT_PASSWORD
          value: ThisIsAlsoSecret
        ports:
        - containerPort: 3306
          name: mysql
        volumeMounts:
        - name: mysql-persistent-storage
          mountPath: /var/lib/mysql
      volumes:
      - name: mysql-persistent-storage
        persistentVolumeClaim:
          claimName: mysql-pv-claim
---
apiVersion: extensions/v1beta1
kind: Ingress
metadata:
  name: wordpress-ingress  
  annotations:
    kubernetes.io/ingress.class: traefik
spec:
  rules:
  - host: wordpress.localhost
    http:
      paths:
      - path: /
        backend:
          serviceName: wordpress
          servicePort: 8081
---
apiVersion: v1
kind: Service
metadata:
  name: wordpress
  labels:
    app: wordpress
spec:
  ports:
    - port: 8081
      targetPort: 80
  selector:
    app: wordpress
    tier: frontend
---
apiVersion: v1
kind: PersistentVolume
metadata:
  name: wp-pv-volume
  labels:
    type: local
spec:
  storageClassName: manual
  capacity:
    storage: 20Gi
  accessModes:
    - ReadWriteOnce
  hostPath:
    path: "/mnt/data"
---
apiVersion: v1
kind: PersistentVolumeClaim
metadata:
  name: wp-pv-claim
  labels:
    app: wordpress
spec:
  storageClassName: manual
  accessModes:
    - ReadWriteOnce
  resources:
    requests:
      storage: 2Gi
---
apiVersion: apps/v1 # for versions before 1.9.0 use apps/v1beta2
kind: Deployment
metadata:
  name: wordpress
  labels:
    app: wordpress
spec:
  selector:
    matchLabels:
      app: wordpress
      tier: frontend
  strategy:
    type: Recreate
  template:
    metadata:
      labels:
        app: wordpress
        tier: frontend
    spec:
      containers:
      - image: wordpress:4.8-apache
        name: wordpress
        env:
        - name: WORDPRESS_DB_HOST
          value: wordpress-mysql
        - name: WORDPRESS_DB_PASSWORD
          value: ThisIsAlsoSecret         
        ports:
        - containerPort: 80
          name: wordpress
        volumeMounts:
        - name: wordpress-persistent-storage
          mountPath: /var/www/html
      volumes:
      - name: wordpress-persistent-storage
        persistentVolumeClaim:
          claimName: wp-pv-claim
```

This yaml will create a Deployment, which will create two pods, one for wordpress and one for mysql. Additionally it will create PV and PVC. It will also provide access to wordpress via the DNS name wordpress.localhost over a Traefik ingressController.

## Part 3 - Rolling out an Ingress Controller and your service.

1. In your UCP installation, set up a Traefik ingressController following these instructions:
https://success.docker.com/article/how-to-configure-traefik-as-a-layer-7-ingress-controller-for-kubernetes

By the end you should have:
- One namespace named `ingress-traefik`
- One up and running `traeffik-ingress-controller` pod
- One nodePort listening on 35080 and one random nodePort. We will use the port 35080 to access our service
- On the random port you can check your Traefik service. It should look like this:
![Kubernetesbasic01](../images/Kubernetesbasic01.png)/

2. Switch to a console with kubectl and your client bundle in place and create a new namespace named `wordpress` with this command:
```
kubectl create namespace wordpress
```
You can check in the UCP frontend or with `kubectl get namespace` if the creation was successful. It should look like this:
```
[admin-str@ee-client01 wordsmith-demo]$ kubectl get namespace
NAME              STATUS    AGE
default           Active    141d
ingress-traefik   Active    139d
kube-public       Active    141d
kube-system       Active    141d
wordpress         Active    28s
```
3. Log into UCP with your admin account. Navigate to Kubernetes and select Namespace. In the Namespace overview, make sure to select wordpress as your context:
![Kubernetesbasic02](../images/Kubernetesbasic02.png)/

4. Now select **Create**, copy the yaml file above, select as namespace `wordpress`, paste the yaml file into the black box, and click create:
![Kubernetesbasic03](../images/Kubernetesbasic03.png)/

The creation of all pods and service will take a while, depending on your internet speed and environment setup.

5. When done correctly, you should see your service up in your Traefik Management:
![Kubernetesbasic04](../images/Kubernetesbasic04.png)/

6. To access your wordpress page, you can either use a real DNS entry of your environment, or in this example, simply add the IP adress of one of your UCP workers to your /etc/hosts file. e.g.: `10.10.10.10 wordpress.localhost`
![Kubernetesbasic05](../images/Kubernetesbasic05.png)/

7. You can now check out all the items you have created with kubectl or with UCP's web frontend:
```
[admin-str@ee-client01 wordsmith-demo]$ kubectl get all --namespace=wordpress
NAME                     AGE
deploy/wordpress         24m
deploy/wordpress-mysql   24m

NAME                            AGE
rs/wordpress-654597bbc4         24m
rs/wordpress-mysql-5c665d9d86   24m

NAME                                  READY     STATUS    RESTARTS   AGE
po/wordpress-654597bbc4-fwsmx         1/1       Running   0          24m
po/wordpress-mysql-5c665d9d86-bwv2m   1/1       Running   0          24m

NAME                  TYPE        CLUSTER-IP    EXTERNAL-IP   PORT(S)    AGE
svc/wordpress         ClusterIP   10.96.7.229   <none>        8081/TCP   5m
svc/wordpress-mysql   ClusterIP   None          <none>        3306/TCP   24m
```

Make yourself familiar with the objects. All objects are quiet common in Kubernetes environments. Please contact your Docker instructor for further help or explanation for this exercise, if necessary.


## Conclusion

Kubernetes provides a very different approach on orchestration management, while the goal stays the same: Automation and simple management of a highly flexible environment. This course scratches only the surface, but shows off a simple app with Kubernetes. 

**Further reading:**

https://kubernetes.io/docs/reference/kubectl/docker-cli-to-kubectl/
https://kubernetes.io/docs/concepts/workloads/controllers/deployment/
https://kubernetes.io/docs/concepts/services-networking/service/
https://kubernetes.io/docs/concepts/services-networking/ingress/
https://kubernetes.io/docs/concepts/storage/persistent-volumes/



