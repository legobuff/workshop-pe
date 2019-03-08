# Deploying Jenkins within a Kubernetes Cluster

By the end of this exercise, you should be able to:

 - Deploy a basic Jenkins Server within Kubernetes
 - Configure the Jenkins Kubernetes Plug-In
 - Combine GitHub, Jenkins and Kubernetes to build a Maven Tomcat-Webapp


## Introduction

This exercise is not build on best practices. There are many different ways to realize a Jenkins Deployment environment. This following example is a working solution but lacks a lot of security related configurations. Our setup will include not the latest versions of different images, does not provide secured or save authentication. It is a basic demo to show the integration with Jenkins in Kubernetes, based on an old Maven example.

Before you begin this exercise you should:
- Be able to use GitHub and provide a GitHub account.
- Have an UCP/DTR installation in place
- Know how to use and where to find a UCP Client Bundle
- Have a fully working repository based on DTR in place

## Part 1 - Building a Jenkins Server Image

We will build our own Jenkins Server Image which we will deploy in our Kubernetes Cluster

1. On a docker host create a `Dockerfile` with the following content:

```
FROM jenkins/jenkins:2.138.4

# Distributed Builds plugins
RUN /usr/local/bin/install-plugins.sh ssh-slaves

# install Notifications and Publishing plugins
RUN /usr/local/bin/install-plugins.sh email-ext
RUN /usr/local/bin/install-plugins.sh mailer
RUN /usr/local/bin/install-plugins.sh slack

# Artifacts
RUN /usr/local/bin/install-plugins.sh htmlpublisher

# UI
RUN /usr/local/bin/install-plugins.sh greenballs
RUN /usr/local/bin/install-plugins.sh simple-theme-plugin

# Scaling
RUN /usr/local/bin/install-plugins.sh kubernetes

# GitHub
RUN /usr/local/bin/install-plugins.sh github


# install Maven
USER root
RUN apt-get update && apt-get install -y maven
USER jenkins
```

Build and Push commands:
```
docker image build -t YOURDTRURL/REPOSITORY/jenkins-server:v1

docker image push YOURDTRURL/REPOSITORY/jenkins-server:v1
```

2. Make sure your client bundle is activated and create a namespace for our deployment:
```
kubectl create namespace jenkins-k8s
```

3. Copy the following YML files and apply them to your namespace `jenkins-k8s`:

**jenkins-k8s-deployment.yml**
```
apiVersion: extensions/v1beta1
kind: Deployment
metadata:
  name: jenkins
  namespace: jenkins-k8s
spec:
  replicas: 1
  template:
    metadata:
      labels:
        app: jenkins
    spec:
      containers:
        - name: jenkins
          image: ee-dtr.sttproductions.de/sttproductions/jenkins-master-k8s:v1
          env:
            - name: JAVA_OPTS
              value: -Djenkins.install.runSetupWizard=false
          ports:
            - name: http-port
              containerPort: 8080
            - name: jnlp-port
              containerPort: 50000
          volumeMounts:
            - name: jenkins-home
              mountPath: /var/jenkins_home
      volumes:
        - name: jenkins-home
          emptyDir: {}
---
apiVersion: v1
kind: Service
metadata:
  name: jenkins
  namespace: jenkins-k8s
spec:
  type: NodePort
  ports:
    - port: 8080
      targetPort: 8080
      nodePort: 35500
  selector:
    app: jenkins
```

**jenkins-k8s-serviceaccount.yml**
```
apiVersion: rbac.authorization.k8s.io/v1beta1
kind: ClusterRoleBinding
metadata:
  name: Jenkins-rbac
subjects:
  - kind: ServiceAccount
    # Reference to upper's `metadata.name`
    name: default
    # Reference to upper's `metadata.namespace`
    namespace: jenkins-k8s
roleRef:
  kind: ClusterRole
  name: cluster-admin
  apiGroup: rbac.authorization.k8s.io
```

Creating our objects:
```
kubectl create -f jenkins-k8s-deployment.yml
kubectl create -f jenkins-k8s-serviceaccount.yml
```

The first file will create the Jenkins Server deployment with an NodePort `35500`. The second file will grant the default jenkins-k8s user a cluster-admin Role.

4. After your deployment is up and running you should be able to access your Jenkins Server by `http://WORKERNODE-URL:35500` - Make sure your Firewall or cloud access allows you to access this NodePort. The NodePort will not change.

![part03-k8sjenkins01](../images/part03-k8sjenkins01.png)/

## Part 2 - Jenkins Plugin Installation and Configuration

You may notice a couple of warning messages, as this is only a test setup, you can ignore or dismiss those messages. We will continue to configure Jenkins with it's plugins first.

1. Select `Manage Jenkins` and select `Manage Plugins`

2. Under `Available` select `Pipelines` and click `Install without restart`

Make sure the Plugins `Pipeline`, `Kubernetes` and `GitHub` are installed correctly.

3. Return to `Manage Jenkins` and select `Global Tool Configuration`

4. Scroll to `Maven` click `Add Maven` and type as name `MVN`. Select version `3.5.4` at the Version drop down. Click `Save`

5. Return to `Manage Jenkins` and select `Nodes`

6. Click on the cock wheel next to the Master Node.

7. Switch `Usage` to `Only build jobs with label expressions matching this node`

8. Click `Save`

## Part 3 - Jenkins Cloud (Kubernetes) Configuration

Next we will configure the Jenkins Kubernetes Plugin, which will integrate in our cluster.

1. Select `Manage Jenkins` and select `Configure System`

2. Scroll down until `Cloud` and click `Add a new Cloud` and select `Kubernetes`

3. Provide the following information:

- Name: Kubernetes
- Kubernetes URL: https://UCPCLUSTERNODE:6443 **NOTE:** This can be either your LoadBalancer or a single UCP Management Node.
- Kubernetes server certificate key: Within your client bundle you will find a CA.pem file. Copy and paste the content of the file in here.

![part03-k8sjenkins02](../images/part03-k8sjenkins02.png)/

- Disable https certificate check: Enabled
- Kubernetes Namespace: jenkins-k8s
- Jenkins URL: http://JENKINS-PODIP:8080 **NOTE:**To receive the Jenkins URL, you will need the kubernetes provided IP. You can find the IP by running: `kubectl -n jenkins-k8s describe pod JENKINSPODNAME* 

![part03-k8sjenkins03](../images/part03-k8sjenkins03.png)/

You should be able to connect to you Cluster by pressing `Test Connection`

4. Next we will create our worker Pods, please click `Add Pod Template`, select `Kubernetes Pod Template` and provide the following information:

- Name: jenkins-slave
- Namespace: jenkins-k8s
- Labels: jenkins-slave
- Usage Use this node as much as possible

![part03-k8sjenkins04](../images/part03-k8sjenkins04.png)/

Click `Add Container` and provide the following information:

- Name: jenkins-slave
- Docker image: jenkinsci/jnlp-slave:3.26-1
- *Leave the rest at their default value* 

![part03-k8sjenkins05](../images/part03-k8sjenkins05.png)/

Click `Add Container` again and provide the following information:

- Name: docker
- Docker image: docker
- *Leave the rest at their default value* 
- Click `Add Volume` and select `Host Path Volume`
 - Add */var/run/docker.sock* for `Host Path` and `Mount Path`

![part03-k8sjenkins06](../images/part03-k8sjenkins06.png)/

Click `Save` to save the changes.



## Part 4 - Creating a Pipeline to build a Maven-Project

For the Pipeline to work. Please clone the following Git: https://github.com/stefantrimborn/k8s-pipeline
You will need to make changes to the following files:
 - Jenkinsfile
 - /webapp/src/main/webapp/index.jsp

1. Within the `Jenkinsfile` make sure your replace all values for your own DTR and provide a `DTR username` and `DTR password` in the Docker step.

2. In Jenkins, select `New Item`, provide an `item name` and select `Pipeline`

3. Select `Advanced Project Options`, select `Pipeline script from SCM`, select `Git` for SCM and provide your Repository URL. Click `Save`.

![part03-k8sjenkins07](../images/part03-k8sjenkins07.png)/

4. In the Pipeline Item press `Build now`

Jenkins will deploy `jenkins-slave-RANDOM-ID` pods to provide as basis for the build steps. A successful build will look like this:

```
Started by user unknown or anonymous
Obtained Jenkinsfile from git https://github.com/stefantrimborn/k8s-pipeline.git
Running in Durability level: MAX_SURVIVABILITY
[Pipeline] Start of Pipeline
[Pipeline] node
Still waiting to schedule task
Waiting for next available executor
Agent jenkins-slave-nv1pr is provisioned from template Kubernetes Pod Template
Agent specification [Kubernetes Pod Template] (jenkins-slave): 
* [jenkins-slave] jenkinsci/jnlp-slave:3.26-1
* [docker] docker

Running on jenkins-slave-nv1pr in /home/jenkins/workspace/First_Pipeline
[Pipeline] {
[Pipeline] stage
[Pipeline] { (Declarative: Checkout SCM)
[Pipeline] checkout
No credentials specified
Cloning the remote Git repository
Cloning repository https://github.com/stefantrimborn/k8s-pipeline.git
 > git init /home/jenkins/workspace/First_Pipeline # timeout=10
Fetching upstream changes from https://github.com/stefantrimborn/k8s-pipeline.git
 > git --version # timeout=10
 > git fetch --tags --progress https://github.com/stefantrimborn/k8s-pipeline.git +refs/heads/*:refs/remotes/origin/*
Checking out Revision cddf3b9d6929571b804c02db1ed5fadb63bc4ba6 (refs/remotes/origin/master)
 > git config remote.origin.url https://github.com/stefantrimborn/k8s-pipeline.git # timeout=10
 > git config --add remote.origin.fetch +refs/heads/*:refs/remotes/origin/* # timeout=10
 > git config remote.origin.url https://github.com/stefantrimborn/k8s-pipeline.git # timeout=10
Fetching upstream changes from https://github.com/stefantrimborn/k8s-pipeline.git
 > git fetch --tags --progress https://github.com/stefantrimborn/k8s-pipeline.git +refs/heads/*:refs/remotes/origin/*
 > git rev-parse refs/remotes/origin/master^{commit} # timeout=10
 > git rev-parse refs/remotes/origin/origin/master^{commit} # timeout=10
 > git config core.sparsecheckout # timeout=10
 > git checkout -f cddf3b9d6929571b804c02db1ed5fadb63bc4ba6
Commit message: "fixes"
[Pipeline] }
 > git rev-list --no-walk 48a57811be41f004d873aa32502c74c9b6ea6d52 # timeout=10
[Pipeline] // stage
[Pipeline] withEnv
[Pipeline] {
[Pipeline] stage
[Pipeline] { (Declarative: Tool Install)
[Pipeline] tool
Unpacking https://repo.maven.apache.org/maven2/org/apache/maven/apache-maven/3.5.4/apache-maven-3.5.4-bin.zip to /home/jenkins/tools/hudson.tasks.Maven_MavenInstallation/MVN on jenkins-slave-nv1pr
[Pipeline] envVarsForTool
[Pipeline] }
[Pipeline] // stage
[Pipeline] withEnv
[Pipeline] {
[Pipeline] stage
[Pipeline] { (Maven Build)
[Pipeline] tool
[Pipeline] envVarsForTool
[Pipeline] withEnv
[Pipeline] {
[Pipeline] container
[Pipeline] {
[Pipeline] sh
+ mvn clean package
[INFO] Scanning for projects...
[WARNING] 
[WARNING] Some problems were encountered while building the effective model for com.example.maven-project:server:jar:1.0-SNAPSHOT
[WARNING] Reporting configuration should be done in <reporting> section, not in maven-site-plugin <configuration> as reportPlugins parameter.
[WARNING] 
[WARNING] Some problems were encountered while building the effective model for com.example.maven-project:webapp:war:1.0-SNAPSHOT
[WARNING] Reporting configuration should be done in <reporting> section, not in maven-site-plugin <configuration> as reportPlugins parameter.
[WARNING] 
[WARNING] Some problems were encountered while building the effective model for com.example.maven-project:maven-project:pom:1.0-SNAPSHOT
[WARNING] Reporting configuration should be done in <reporting> section, not in maven-site-plugin <configuration> as reportPlugins parameter. @ line 51, column 24
[WARNING] 
[WARNING] It is highly recommended to fix these problems because they threaten the stability of your build.
[WARNING] 
[WARNING] For this reason, future Maven versions might no longer support building such malformed projects.
[WARNING] 
[WARNING] The project com.example.maven-project:maven-project:pom:1.0-SNAPSHOT uses prerequisites which is only intended for maven-plugin projects but not for non maven-plugin projects. For such purposes you should use the maven-enforcer-plugin. See https://maven.apache.org/enforcer/enforcer-rules/requireMavenVersion.html
[INFO] ------------------------------------------------------------------------
[INFO] Reactor Build Order:
[INFO] 
[INFO] Maven Project                                                      [pom]
[INFO] Server                                                             [jar]
[INFO] Webapp                                                             [war]
[INFO] 
[INFO] --------------< com.example.maven-project:maven-project >---------------
[INFO] Building Maven Project 1.0-SNAPSHOT                                [1/3]
[INFO] --------------------------------[ pom ]---------------------------------
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-clean-plugin/2.5/maven-clean-plugin-2.5.pom
Progress (1): 2.2/3.9 kB
Progress (1): 3.9 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-clean-plugin/2.5/maven-clean-plugin-2.5.pom (3.9 kB at 11 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-plugins/22/maven-plugins-22.pom
Progress (1): 2.2/13 kB
Progress (1): 5.0/13 kB
Progress (1): 7.8/13 kB
Progress (1): 11/13 kB 
Progress (1): 13 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-plugins/22/maven-plugins-22.pom (13 kB at 407 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/21/maven-parent-21.pom
Progress (1): 2.8/26 kB
Progress (1): 5.5/26 kB
Progress (1): 8.3/26 kB
Progress (1): 11/26 kB 
Progress (1): 14/26 kB
Progress (1): 16/26 kB
Progress (1): 19/26 kB
Progress (1): 22/26 kB
Progress (1): 25/26 kB
Progress (1): 26 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/21/maven-parent-21.pom (26 kB at 586 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/apache/10/apache-10.pom
Progress (1): 2.8/15 kB
Progress (1): 5.5/15 kB
Progress (1): 8.3/15 kB
Progress (1): 11/15 kB 
Progress (1): 14/15 kB
Progress (1): 15 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/apache/10/apache-10.pom (15 kB at 379 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-clean-plugin/2.5/maven-clean-plugin-2.5.jar
Progress (1): 2.2/25 kB
Progress (1): 5.0/25 kB
Progress (1): 7.7/25 kB
Progress (1): 10/25 kB 
Progress (1): 13/25 kB
Progress (1): 16/25 kB
Progress (1): 19/25 kB
Progress (1): 21/25 kB
Progress (1): 24/25 kB
Progress (1): 25 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-clean-plugin/2.5/maven-clean-plugin-2.5.jar (25 kB at 746 kB/s)
[INFO] 
[INFO] --- maven-clean-plugin:2.5:clean (default-clean) @ maven-project ---
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-api/2.0.6/maven-plugin-api-2.0.6.pom
Progress (1): 1.5 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-api/2.0.6/maven-plugin-api-2.0.6.pom (1.5 kB at 54 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven/2.0.6/maven-2.0.6.pom
Progress (1): 2.2/9.0 kB
Progress (1): 5.0/9.0 kB
Progress (1): 7.8/9.0 kB
Progress (1): 9.0 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven/2.0.6/maven-2.0.6.pom (9.0 kB at 292 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/5/maven-parent-5.pom
Progress (1): 2.8/15 kB
Progress (1): 5.5/15 kB
Progress (1): 8.3/15 kB
Progress (1): 11/15 kB 
Progress (1): 14/15 kB
Progress (1): 15 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/5/maven-parent-5.pom (15 kB at 435 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/apache/3/apache-3.pom
Progress (1): 2.8/3.4 kB
Progress (1): 3.4 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/apache/3/apache-3.pom (3.4 kB at 107 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/3.0/plexus-utils-3.0.pom
Progress (1): 2.2/4.1 kB
Progress (1): 4.1 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/3.0/plexus-utils-3.0.pom (4.1 kB at 123 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/sonatype/spice/spice-parent/16/spice-parent-16.pom
Progress (1): 2.2/8.4 kB
Progress (1): 5.0/8.4 kB
Progress (1): 7.7/8.4 kB
Progress (1): 8.4 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/sonatype/spice/spice-parent/16/spice-parent-16.pom (8.4 kB at 204 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/sonatype/forge/forge-parent/5/forge-parent-5.pom
Progress (1): 2.8/8.4 kB
Progress (1): 5.5/8.4 kB
Progress (1): 8.3/8.4 kB
Progress (1): 8.4 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/sonatype/forge/forge-parent/5/forge-parent-5.pom (8.4 kB at 214 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/3.0/plexus-utils-3.0.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-api/2.0.6/maven-plugin-api-2.0.6.jar
Progress (1): 2.2/13 kB
Progress (1): 5.0/13 kB
Progress (1): 7.7/13 kB
Progress (1): 10/13 kB 
Progress (1): 13 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-api/2.0.6/maven-plugin-api-2.0.6.jar (13 kB at 306 kB/s)
Progress (1): 2.8/226 kB
Progress (1): 5.5/226 kB
Progress (1): 8.3/226 kB
Progress (1): 11/226 kB 
Progress (1): 14/226 kB
Progress (1): 16/226 kB
Progress (1): 19/226 kB
Progress (1): 22/226 kB
Progress (1): 25/226 kB
Progress (1): 27/226 kB
Progress (1): 30/226 kB
Progress (1): 33/226 kB
Progress (1): 37/226 kB
Progress (1): 41/226 kB
Progress (1): 45/226 kB
Progress (1): 49/226 kB
Progress (1): 53/226 kB
Progress (1): 57/226 kB
Progress (1): 61/226 kB
Progress (1): 65/226 kB
Progress (1): 66/226 kB
Progress (1): 70/226 kB
Progress (1): 74/226 kB
Progress (1): 78/226 kB
Progress (1): 82/226 kB
Progress (1): 86/226 kB
Progress (1): 90/226 kB
Progress (1): 94/226 kB
Progress (1): 98/226 kB
Progress (1): 102/226 kB
Progress (1): 106/226 kB
Progress (1): 111/226 kB
Progress (1): 115/226 kB
Progress (1): 119/226 kB
Progress (1): 123/226 kB
Progress (1): 127/226 kB
Progress (1): 131/226 kB
Progress (1): 135/226 kB
Progress (1): 139/226 kB
Progress (1): 143/226 kB
Progress (1): 147/226 kB
Progress (1): 152/226 kB
Progress (1): 156/226 kB
Progress (1): 160/226 kB
Progress (1): 164/226 kB
Progress (1): 168/226 kB
Progress (1): 172/226 kB
Progress (1): 176/226 kB
Progress (1): 180/226 kB
Progress (1): 184/226 kB
Progress (1): 188/226 kB
Progress (1): 193/226 kB
Progress (1): 197/226 kB
Progress (1): 201/226 kB
Progress (1): 205/226 kB
Progress (1): 209/226 kB
Progress (1): 213/226 kB
Progress (1): 217/226 kB
Progress (1): 221/226 kB
Progress (1): 225/226 kB
Progress (1): 226 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/3.0/plexus-utils-3.0.jar (226 kB at 930 kB/s)
[INFO] 
[INFO] ------------------< com.example.maven-project:server >------------------
[INFO] Building Server 1.0-SNAPSHOT                                       [2/3]
[INFO] --------------------------------[ jar ]---------------------------------
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-resources-plugin/2.5/maven-resources-plugin-2.5.pom
Progress (1): 4.1/7.1 kB
Progress (1): 7.1 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-resources-plugin/2.5/maven-resources-plugin-2.5.pom (7.1 kB at 229 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-plugins/19/maven-plugins-19.pom
Progress (1): 2.2/11 kB
Progress (1): 5.0/11 kB
Progress (1): 7.8/11 kB
Progress (1): 11/11 kB 
Progress (1): 11 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-plugins/19/maven-plugins-19.pom (11 kB at 355 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/19/maven-parent-19.pom
Progress (1): 2.8/25 kB
Progress (1): 5.5/25 kB
Progress (1): 8.3/25 kB
Progress (1): 11/25 kB 
Progress (1): 14/25 kB
Progress (1): 16/25 kB
Progress (1): 19/25 kB
Progress (1): 22/25 kB
Progress (1): 25/25 kB
Progress (1): 25 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/19/maven-parent-19.pom (25 kB at 781 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/apache/9/apache-9.pom
Progress (1): 2.2/15 kB
Progress (1): 5.0/15 kB
Progress (1): 7.7/15 kB
Progress (1): 11/15 kB 
Progress (1): 13/15 kB
Progress (1): 15 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/apache/9/apache-9.pom (15 kB at 474 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-resources-plugin/2.5/maven-resources-plugin-2.5.jar
Progress (1): 2.2/26 kB
Progress (1): 5.0/26 kB
Progress (1): 7.7/26 kB
Progress (1): 10/26 kB 
Progress (1): 13/26 kB
Progress (1): 16/26 kB
Progress (1): 19/26 kB
Progress (1): 21/26 kB
Progress (1): 24/26 kB
Progress (1): 26 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-resources-plugin/2.5/maven-resources-plugin-2.5.jar (26 kB at 671 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-compiler-plugin/2.3.2/maven-compiler-plugin-2.3.2.pom
Progress (1): 2.8/7.3 kB
Progress (1): 5.5/7.3 kB
Progress (1): 7.3 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-compiler-plugin/2.3.2/maven-compiler-plugin-2.3.2.pom (7.3 kB at 209 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-plugins/18/maven-plugins-18.pom
Progress (1): 2.8/13 kB
Progress (1): 5.5/13 kB
Progress (1): 8.3/13 kB
Progress (1): 11/13 kB 
Progress (1): 13 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-plugins/18/maven-plugins-18.pom (13 kB at 382 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/16/maven-parent-16.pom
Progress (1): 2.8/23 kB
Progress (1): 5.5/23 kB
Progress (1): 8.3/23 kB
Progress (1): 11/23 kB 
Progress (1): 14/23 kB
Progress (1): 16/23 kB
Progress (1): 19/23 kB
Progress (1): 22/23 kB
Progress (1): 23 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/16/maven-parent-16.pom (23 kB at 582 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/apache/7/apache-7.pom
Progress (1): 2.2/14 kB
Progress (1): 5.0/14 kB
Progress (1): 7.7/14 kB
Progress (1): 11/14 kB 
Progress (1): 13/14 kB
Progress (1): 14 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/apache/7/apache-7.pom (14 kB at 481 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-compiler-plugin/2.3.2/maven-compiler-plugin-2.3.2.jar
Progress (1): 2.2/29 kB
Progress (1): 5.0/29 kB
Progress (1): 7.7/29 kB
Progress (1): 10/29 kB 
Progress (1): 13/29 kB
Progress (1): 16/29 kB
Progress (1): 19/29 kB
Progress (1): 21/29 kB
Progress (1): 24/29 kB
Progress (1): 27/29 kB
Progress (1): 29 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-compiler-plugin/2.3.2/maven-compiler-plugin-2.3.2.jar (29 kB at 695 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-surefire-plugin/2.11/maven-surefire-plugin-2.11.pom
Progress (1): 2.2/11 kB
Progress (1): 5.0/11 kB
Progress (1): 7.8/11 kB
Progress (1): 11/11 kB 
Progress (1): 11 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-surefire-plugin/2.11/maven-surefire-plugin-2.11.pom (11 kB at 359 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire/2.11/surefire-2.11.pom
Progress (1): 2.2/12 kB
Progress (1): 5.0/12 kB
Progress (1): 7.8/12 kB
Progress (1): 11/12 kB 
Progress (1): 12 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire/2.11/surefire-2.11.pom (12 kB at 352 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/20/maven-parent-20.pom
Progress (1): 2.8/25 kB
Progress (1): 5.5/25 kB
Progress (1): 8.3/25 kB
Progress (1): 11/25 kB 
Progress (1): 14/25 kB
Progress (1): 16/25 kB
Progress (1): 19/25 kB
Progress (1): 22/25 kB
Progress (1): 25 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/20/maven-parent-20.pom (25 kB at 684 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-surefire-plugin/2.11/maven-surefire-plugin-2.11.jar
Progress (1): 2.2/31 kB
Progress (1): 5.0/31 kB
Progress (1): 7.7/31 kB
Progress (1): 10/31 kB 
Progress (1): 13/31 kB
Progress (1): 16/31 kB
Progress (1): 19/31 kB
Progress (1): 21/31 kB
Progress (1): 24/31 kB
Progress (1): 27/31 kB
Progress (1): 30/31 kB
Progress (1): 31 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-surefire-plugin/2.11/maven-surefire-plugin-2.11.jar (31 kB at 505 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-jar-plugin/2.4/maven-jar-plugin-2.4.pom
Progress (1): 2.8/5.8 kB
Progress (1): 5.5/5.8 kB
Progress (1): 5.8 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-jar-plugin/2.4/maven-jar-plugin-2.4.pom (5.8 kB at 201 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-jar-plugin/2.4/maven-jar-plugin-2.4.jar
Progress (1): 2.2/34 kB
Progress (1): 5.0/34 kB
Progress (1): 7.7/34 kB
Progress (1): 10/34 kB 
Progress (1): 13/34 kB
Progress (1): 16/34 kB
Progress (1): 19/34 kB
Progress (1): 21/34 kB
Progress (1): 24/34 kB
Progress (1): 27/34 kB
Progress (1): 30/34 kB
Progress (1): 32/34 kB
Progress (1): 34 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-jar-plugin/2.4/maven-jar-plugin-2.4.jar (34 kB at 872 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/junit/junit-dep/4.10/junit-dep-4.10.pom
Progress (1): 2.2/2.3 kB
Progress (1): 2.3 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/junit/junit-dep/4.10/junit-dep-4.10.pom (2.3 kB at 48 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/hamcrest/hamcrest-core/1.2.1/hamcrest-core-1.2.1.pom
Progress (1): 2.2/5.3 kB
Progress (1): 5.0/5.3 kB
Progress (1): 5.3 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/hamcrest/hamcrest-core/1.2.1/hamcrest-core-1.2.1.pom (5.3 kB at 177 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/hamcrest/hamcrest-library/1.2.1/hamcrest-library-1.2.1.pom
Progress (1): 2.2/5.5 kB
Progress (1): 5.0/5.5 kB
Progress (1): 5.5 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/hamcrest/hamcrest-library/1.2.1/hamcrest-library-1.2.1.pom (5.5 kB at 104 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/mockito/mockito-core/1.8.5/mockito-core-1.8.5.pom
Progress (1): 1.3 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/mockito/mockito-core/1.8.5/mockito-core-1.8.5.pom (1.3 kB at 36 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/objenesis/objenesis/1.0/objenesis-1.0.pom
Progress (1): 853 B
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/objenesis/objenesis/1.0/objenesis-1.0.pom (853 B at 25 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/junit/junit-dep/4.10/junit-dep-4.10.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/hamcrest/hamcrest-core/1.2.1/hamcrest-core-1.2.1.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/hamcrest/hamcrest-library/1.2.1/hamcrest-library-1.2.1.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/mockito/mockito-core/1.8.5/mockito-core-1.8.5.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/objenesis/objenesis/1.0/objenesis-1.0.jar
Progress (1): 2.2/234 kB
Progress (2): 2.2/234 kB | 0/1.3 MB
Progress (2): 2.2/234 kB | 0/1.3 MB
Progress (2): 5.0/234 kB | 0/1.3 MB
Progress (2): 5.0/234 kB | 0/1.3 MB
Progress (2): 5.0/234 kB | 0/1.3 MB
Progress (2): 7.7/234 kB | 0/1.3 MB
Progress (2): 7.7/234 kB | 0/1.3 MB
Progress (2): 10/234 kB | 0/1.3 MB 
Progress (2): 10/234 kB | 0/1.3 MB
Progress (2): 13/234 kB | 0/1.3 MB
Progress (2): 13/234 kB | 0/1.3 MB
Progress (2): 16/234 kB | 0/1.3 MB
Progress (2): 16/234 kB | 0/1.3 MB
Progress (2): 19/234 kB | 0/1.3 MB
Progress (2): 19/234 kB | 0/1.3 MB
Progress (2): 19/234 kB | 0/1.3 MB
Progress (2): 19/234 kB | 0/1.3 MB
Progress (2): 19/234 kB | 0/1.3 MB
Progress (2): 21/234 kB | 0/1.3 MB
Progress (2): 24/234 kB | 0/1.3 MB
Progress (2): 27/234 kB | 0/1.3 MB
Progress (2): 30/234 kB | 0/1.3 MB
Progress (2): 32/234 kB | 0/1.3 MB
Progress (2): 32/234 kB | 0/1.3 MB
Progress (2): 32/234 kB | 0/1.3 MB
Progress (2): 32/234 kB | 0/1.3 MB
Progress (2): 32/234 kB | 0/1.3 MB
Progress (2): 32/234 kB | 0.1/1.3 MB
Progress (2): 32/234 kB | 0.1/1.3 MB
Progress (2): 32/234 kB | 0.1/1.3 MB
Progress (2): 32/234 kB | 0.1/1.3 MB
Progress (2): 32/234 kB | 0.1/1.3 MB
Progress (2): 32/234 kB | 0.1/1.3 MB
Progress (2): 32/234 kB | 0.1/1.3 MB
Progress (2): 36/234 kB | 0.1/1.3 MB
Progress (2): 36/234 kB | 0.1/1.3 MB
Progress (3): 36/234 kB | 0.1/1.3 MB | 2.2/50 kB
Progress (3): 40/234 kB | 0.1/1.3 MB | 2.2/50 kB
Progress (3): 40/234 kB | 0.1/1.3 MB | 5.0/50 kB
Progress (3): 45/234 kB | 0.1/1.3 MB | 5.0/50 kB
Progress (3): 45/234 kB | 0.1/1.3 MB | 7.7/50 kB
Progress (3): 49/234 kB | 0.1/1.3 MB | 7.7/50 kB
Progress (3): 49/234 kB | 0.1/1.3 MB | 10/50 kB 
Progress (3): 49/234 kB | 0.1/1.3 MB | 13/50 kB
Progress (3): 49/234 kB | 0.1/1.3 MB | 13/50 kB
Progress (3): 53/234 kB | 0.1/1.3 MB | 13/50 kB
Progress (3): 57/234 kB | 0.1/1.3 MB | 13/50 kB
Progress (3): 61/234 kB | 0.1/1.3 MB | 13/50 kB
Progress (3): 65/234 kB | 0.1/1.3 MB | 13/50 kB
Progress (3): 69/234 kB | 0.1/1.3 MB | 13/50 kB
Progress (3): 73/234 kB | 0.1/1.3 MB | 13/50 kB
Progress (3): 73/234 kB | 0.1/1.3 MB | 13/50 kB
Progress (3): 73/234 kB | 0.1/1.3 MB | 13/50 kB
Progress (3): 73/234 kB | 0.1/1.3 MB | 13/50 kB
Progress (3): 73/234 kB | 0.1/1.3 MB | 13/50 kB
Progress (3): 73/234 kB | 0.1/1.3 MB | 13/50 kB
Progress (3): 73/234 kB | 0.1/1.3 MB | 13/50 kB
Progress (3): 73/234 kB | 0.1/1.3 MB | 16/50 kB
Progress (3): 77/234 kB | 0.1/1.3 MB | 16/50 kB
Progress (3): 77/234 kB | 0.1/1.3 MB | 16/50 kB
Progress (3): 77/234 kB | 0.1/1.3 MB | 19/50 kB
Progress (3): 81/234 kB | 0.1/1.3 MB | 19/50 kB
Progress (3): 81/234 kB | 0.1/1.3 MB | 21/50 kB
Progress (3): 81/234 kB | 0.1/1.3 MB | 24/50 kB
Progress (3): 85/234 kB | 0.1/1.3 MB | 24/50 kB
Progress (4): 85/234 kB | 0.1/1.3 MB | 24/50 kB | 2.2/42 kB
Progress (4): 85/234 kB | 0.1/1.3 MB | 24/50 kB | 2.2/42 kB
Progress (4): 85/234 kB | 0.1/1.3 MB | 27/50 kB | 2.2/42 kB
Progress (4): 90/234 kB | 0.1/1.3 MB | 27/50 kB | 2.2/42 kB
Progress (4): 90/234 kB | 0.1/1.3 MB | 27/50 kB | 5.0/42 kB
Progress (4): 90/234 kB | 0.1/1.3 MB | 27/50 kB | 7.7/42 kB
Progress (4): 90/234 kB | 0.1/1.3 MB | 27/50 kB | 10/42 kB 
Progress (4): 90/234 kB | 0.1/1.3 MB | 27/50 kB | 13/42 kB
Progress (4): 94/234 kB | 0.1/1.3 MB | 27/50 kB | 13/42 kB
Progress (4): 94/234 kB | 0.1/1.3 MB | 27/50 kB | 13/42 kB
Progress (4): 94/234 kB | 0.1/1.3 MB | 30/50 kB | 13/42 kB
Progress (4): 98/234 kB | 0.1/1.3 MB | 30/50 kB | 13/42 kB
Progress (4): 98/234 kB | 0.1/1.3 MB | 32/50 kB | 13/42 kB
Progress (4): 102/234 kB | 0.1/1.3 MB | 32/50 kB | 13/42 kB
Progress (4): 102/234 kB | 0.1/1.3 MB | 32/50 kB | 13/42 kB
Progress (4): 102/234 kB | 0.1/1.3 MB | 36/50 kB | 13/42 kB
Progress (4): 106/234 kB | 0.1/1.3 MB | 36/50 kB | 13/42 kB
Progress (4): 106/234 kB | 0.1/1.3 MB | 36/50 kB | 13/42 kB
Progress (4): 106/234 kB | 0.1/1.3 MB | 40/50 kB | 13/42 kB
Progress (4): 106/234 kB | 0.1/1.3 MB | 40/50 kB | 16/42 kB
Progress (4): 106/234 kB | 0.1/1.3 MB | 40/50 kB | 19/42 kB
Progress (4): 106/234 kB | 0.1/1.3 MB | 40/50 kB | 21/42 kB
Progress (4): 106/234 kB | 0.1/1.3 MB | 40/50 kB | 24/42 kB
Progress (4): 106/234 kB | 0.1/1.3 MB | 40/50 kB | 27/42 kB
Progress (4): 106/234 kB | 0.1/1.3 MB | 40/50 kB | 30/42 kB
Progress (4): 106/234 kB | 0.1/1.3 MB | 40/50 kB | 32/42 kB
Progress (4): 110/234 kB | 0.1/1.3 MB | 40/50 kB | 32/42 kB
Progress (4): 110/234 kB | 0.1/1.3 MB | 40/50 kB | 32/42 kB
Progress (4): 110/234 kB | 0.1/1.3 MB | 45/50 kB | 32/42 kB
Progress (4): 110/234 kB | 0.1/1.3 MB | 49/50 kB | 32/42 kB
Progress (4): 110/234 kB | 0.1/1.3 MB | 50 kB | 32/42 kB   
Progress (5): 110/234 kB | 0.1/1.3 MB | 50 kB | 32/42 kB | 2.8/29 kB
Progress (5): 110/234 kB | 0.1/1.3 MB | 50 kB | 32/42 kB | 5.5/29 kB
Progress (5): 110/234 kB | 0.1/1.3 MB | 50 kB | 32/42 kB | 5.5/29 kB
Progress (5): 114/234 kB | 0.1/1.3 MB | 50 kB | 32/42 kB | 5.5/29 kB
Progress (5): 114/234 kB | 0.1/1.3 MB | 50 kB | 32/42 kB | 5.5/29 kB
Progress (5): 114/234 kB | 0.1/1.3 MB | 50 kB | 32/42 kB | 8.3/29 kB
Progress (5): 114/234 kB | 0.1/1.3 MB | 50 kB | 32/42 kB | 8.3/29 kB
Progress (5): 114/234 kB | 0.1/1.3 MB | 50 kB | 32/42 kB | 11/29 kB 
Progress (5): 114/234 kB | 0.2/1.3 MB | 50 kB | 32/42 kB | 11/29 kB
Progress (5): 114/234 kB | 0.2/1.3 MB | 50 kB | 32/42 kB | 11/29 kB
Progress (5): 114/234 kB | 0.2/1.3 MB | 50 kB | 36/42 kB | 11/29 kB
Progress (5): 114/234 kB | 0.2/1.3 MB | 50 kB | 36/42 kB | 11/29 kB
Progress (5): 114/234 kB | 0.2/1.3 MB | 50 kB | 36/42 kB | 11/29 kB
Progress (5): 114/234 kB | 0.2/1.3 MB | 50 kB | 36/42 kB | 11/29 kB
Progress (5): 114/234 kB | 0.2/1.3 MB | 50 kB | 36/42 kB | 11/29 kB
Progress (5): 114/234 kB | 0.2/1.3 MB | 50 kB | 36/42 kB | 11/29 kB
Progress (5): 118/234 kB | 0.2/1.3 MB | 50 kB | 36/42 kB | 11/29 kB
Progress (5): 122/234 kB | 0.2/1.3 MB | 50 kB | 36/42 kB | 11/29 kB
Progress (5): 126/234 kB | 0.2/1.3 MB | 50 kB | 36/42 kB | 11/29 kB
Progress (5): 131/234 kB | 0.2/1.3 MB | 50 kB | 36/42 kB | 11/29 kB
Progress (5): 135/234 kB | 0.2/1.3 MB | 50 kB | 36/42 kB | 11/29 kB
Progress (5): 139/234 kB | 0.2/1.3 MB | 50 kB | 36/42 kB | 11/29 kB
Progress (5): 143/234 kB | 0.2/1.3 MB | 50 kB | 36/42 kB | 11/29 kB
Progress (5): 147/234 kB | 0.2/1.3 MB | 50 kB | 36/42 kB | 11/29 kB
Progress (5): 147/234 kB | 0.2/1.3 MB | 50 kB | 36/42 kB | 11/29 kB
Progress (5): 147/234 kB | 0.2/1.3 MB | 50 kB | 40/42 kB | 11/29 kB
Progress (5): 147/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 11/29 kB   
Progress (5): 147/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 11/29 kB
Progress (5): 147/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 11/29 kB
Progress (5): 147/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 11/29 kB
Progress (5): 147/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 14/29 kB
Progress (5): 147/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 14/29 kB
Progress (5): 147/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 16/29 kB
Progress (5): 147/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 16/29 kB
Progress (5): 147/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 16/29 kB
Progress (5): 147/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 16/29 kB
Progress (5): 151/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 16/29 kB
Progress (5): 151/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 19/29 kB
Progress (5): 151/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 22/29 kB
Progress (5): 151/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 25/29 kB
Progress (5): 151/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 27/29 kB
Progress (5): 151/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 29 kB   
Progress (5): 151/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 29 kB
Progress (5): 155/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 29 kB
Progress (5): 159/234 kB | 0.2/1.3 MB | 50 kB | 42 kB | 29 kB
                                                             
Downloaded from central: https://repo.maven.apache.org/maven2/org/hamcrest/hamcrest-library/1.2.1/hamcrest-library-1.2.1.jar (50 kB at 471 kB/s)
Progress (4): 159/234 kB | 0.2/1.3 MB | 42 kB | 29 kB
Progress (4): 159/234 kB | 0.2/1.3 MB | 42 kB | 29 kB
Progress (4): 159/234 kB | 0.2/1.3 MB | 42 kB | 29 kB
Progress (4): 159/234 kB | 0.2/1.3 MB | 42 kB | 29 kB
Progress (4): 163/234 kB | 0.2/1.3 MB | 42 kB | 29 kB
Progress (4): 163/234 kB | 0.2/1.3 MB | 42 kB | 29 kB
Progress (4): 163/234 kB | 0.2/1.3 MB | 42 kB | 29 kB
Progress (4): 163/234 kB | 0.2/1.3 MB | 42 kB | 29 kB
Progress (4): 163/234 kB | 0.2/1.3 MB | 42 kB | 29 kB
                                                     
Downloaded from central: https://repo.maven.apache.org/maven2/org/hamcrest/hamcrest-core/1.2.1/hamcrest-core-1.2.1.jar (42 kB at 355 kB/s)
Progress (3): 163/234 kB | 0.2/1.3 MB | 29 kB
Progress (3): 163/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 163/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 163/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 163/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 167/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 167/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 171/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 171/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 176/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 180/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 184/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 188/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 192/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 196/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 200/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 204/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 208/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 212/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 212/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 212/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 212/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 212/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 212/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 212/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 212/234 kB | 0.3/1.3 MB | 29 kB
Progress (3): 212/234 kB | 0.3/1.3 MB | 29 kB
                                             
Downloaded from central: https://repo.maven.apache.org/maven2/org/objenesis/objenesis/1.0/objenesis-1.0.jar (29 kB at 244 kB/s)
Progress (2): 217/234 kB | 0.3/1.3 MB
Progress (2): 221/234 kB | 0.3/1.3 MB
Progress (2): 225/234 kB | 0.3/1.3 MB
Progress (2): 229/234 kB | 0.3/1.3 MB
Progress (2): 233/234 kB | 0.3/1.3 MB
Progress (2): 233/234 kB | 0.3/1.3 MB
Progress (2): 233/234 kB | 0.3/1.3 MB
Progress (2): 233/234 kB | 0.3/1.3 MB
Progress (2): 233/234 kB | 0.3/1.3 MB
Progress (2): 233/234 kB | 0.3/1.3 MB
Progress (2): 233/234 kB | 0.3/1.3 MB
Progress (2): 233/234 kB | 0.3/1.3 MB
Progress (2): 233/234 kB | 0.3/1.3 MB
Progress (2): 233/234 kB | 0.3/1.3 MB
Progress (2): 234 kB | 0.3/1.3 MB    
Progress (2): 234 kB | 0.3/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
Progress (2): 234 kB | 0.4/1.3 MB
                                 
Downloaded from central: https://repo.maven.apache.org/maven2/junit/junit-dep/4.10/junit-dep-4.10.jar (234 kB at 1.3 MB/s)
Progress (1): 0.4/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.5/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.6/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.7/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.8/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 0.9/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.0/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.1/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.2/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3/1.3 MB
Progress (1): 1.3 MB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/mockito/mockito-core/1.8.5/mockito-core-1.8.5.jar (1.3 MB at 5.0 MB/s)
[INFO] 
[INFO] --- maven-clean-plugin:2.5:clean (default-clean) @ server ---
[INFO] Deleting /home/jenkins/workspace/First_Pipeline/server/target
[INFO] 
[INFO] --- maven-resources-plugin:2.5:resources (default-resources) @ server ---
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-project/2.0.6/maven-project-2.0.6.pom
Progress (1): 2.6 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-project/2.0.6/maven-project-2.0.6.pom (2.6 kB at 75 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-settings/2.0.6/maven-settings-2.0.6.pom
Progress (1): 2.0 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-settings/2.0.6/maven-settings-2.0.6.pom (2.0 kB at 74 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-model/2.0.6/maven-model-2.0.6.pom
Progress (1): 0/3.0 kB
Progress (1): 2.8/3.0 kB
Progress (1): 3.0 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-model/2.0.6/maven-model-2.0.6.pom (3.0 kB at 92 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/1.4.1/plexus-utils-1.4.1.pom
Progress (1): 0.9/1.9 kB
Progress (1): 1.9 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/1.4.1/plexus-utils-1.4.1.pom (1.9 kB at 64 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/1.0.11/plexus-1.0.11.pom
Progress (1): 2.2/9.0 kB
Progress (1): 5.0/9.0 kB
Progress (1): 7.8/9.0 kB
Progress (1): 9.0 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/1.0.11/plexus-1.0.11.pom (9.0 kB at 320 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-container-default/1.0-alpha-9-stable-1/plexus-container-default-1.0-alpha-9-stable-1.pom
Progress (1): 2.2/3.9 kB
Progress (1): 3.9 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-container-default/1.0-alpha-9-stable-1/plexus-container-default-1.0-alpha-9-stable-1.pom (3.9 kB at 165 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-containers/1.0.3/plexus-containers-1.0.3.pom
Progress (1): 492 B
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-containers/1.0.3/plexus-containers-1.0.3.pom (492 B at 18 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/1.0.4/plexus-1.0.4.pom
Progress (1): 2.2/5.7 kB
Progress (1): 5.0/5.7 kB
Progress (1): 5.7 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/1.0.4/plexus-1.0.4.pom (5.7 kB at 221 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/junit/junit/3.8.1/junit-3.8.1.pom
Progress (1): 998 B
                   
Downloaded from central: https://repo.maven.apache.org/maven2/junit/junit/3.8.1/junit-3.8.1.pom (998 B at 43 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/1.0.4/plexus-utils-1.0.4.pom
Progress (1): 2.2/6.9 kB
Progress (1): 4.2/6.9 kB
Progress (1): 6.9 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/1.0.4/plexus-utils-1.0.4.pom (6.9 kB at 264 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/classworlds/classworlds/1.1-alpha-2/classworlds-1.1-alpha-2.pom
Progress (1): 2.2/3.1 kB
Progress (1): 3.1 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/classworlds/classworlds/1.1-alpha-2/classworlds-1.1-alpha-2.pom (3.1 kB at 120 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-profile/2.0.6/maven-profile-2.0.6.pom
Progress (1): 2.0 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-profile/2.0.6/maven-profile-2.0.6.pom (2.0 kB at 54 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact-manager/2.0.6/maven-artifact-manager-2.0.6.pom
Progress (1): 2.2/2.6 kB
Progress (1): 2.6 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact-manager/2.0.6/maven-artifact-manager-2.0.6.pom (2.6 kB at 90 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-repository-metadata/2.0.6/maven-repository-metadata-2.0.6.pom
Progress (1): 1.9 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-repository-metadata/2.0.6/maven-repository-metadata-2.0.6.pom (1.9 kB at 71 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact/2.0.6/maven-artifact-2.0.6.pom
Progress (1): 1.6 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact/2.0.6/maven-artifact-2.0.6.pom (1.6 kB at 61 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-registry/2.0.6/maven-plugin-registry-2.0.6.pom
Progress (1): 1.9 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-registry/2.0.6/maven-plugin-registry-2.0.6.pom (1.9 kB at 63 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-core/2.0.6/maven-core-2.0.6.pom
Progress (1): 2.8/6.7 kB
Progress (1): 5.5/6.7 kB
Progress (1): 6.7 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-core/2.0.6/maven-core-2.0.6.pom (6.7 kB at 231 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-parameter-documenter/2.0.6/maven-plugin-parameter-documenter-2.0.6.pom
Progress (1): 1.9 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-parameter-documenter/2.0.6/maven-plugin-parameter-documenter-2.0.6.pom (1.9 kB at 64 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/reporting/maven-reporting-api/2.0.6/maven-reporting-api-2.0.6.pom
Progress (1): 1.8 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/reporting/maven-reporting-api/2.0.6/maven-reporting-api-2.0.6.pom (1.8 kB at 67 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/reporting/maven-reporting/2.0.6/maven-reporting-2.0.6.pom
Progress (1): 1.4 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/reporting/maven-reporting/2.0.6/maven-reporting-2.0.6.pom (1.4 kB at 44 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/doxia/doxia-sink-api/1.0-alpha-7/doxia-sink-api-1.0-alpha-7.pom
Progress (1): 424 B
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/doxia/doxia-sink-api/1.0-alpha-7/doxia-sink-api-1.0-alpha-7.pom (424 B at 16 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/doxia/doxia/1.0-alpha-7/doxia-1.0-alpha-7.pom
Progress (1): 2.2/3.9 kB
Progress (1): 3.9 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/doxia/doxia/1.0-alpha-7/doxia-1.0-alpha-7.pom (3.9 kB at 122 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-error-diagnostics/2.0.6/maven-error-diagnostics-2.0.6.pom
Progress (1): 1.7 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-error-diagnostics/2.0.6/maven-error-diagnostics-2.0.6.pom (1.7 kB at 44 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/commons-cli/commons-cli/1.0/commons-cli-1.0.pom
Progress (1): 2.1 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/commons-cli/commons-cli/1.0/commons-cli-1.0.pom (2.1 kB at 73 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-descriptor/2.0.6/maven-plugin-descriptor-2.0.6.pom
Progress (1): 2.0 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-descriptor/2.0.6/maven-plugin-descriptor-2.0.6.pom (2.0 kB at 70 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interactivity-api/1.0-alpha-4/plexus-interactivity-api-1.0-alpha-4.pom
Progress (1): 2.2/7.1 kB
Progress (1): 5.0/7.1 kB
Progress (1): 7.1 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interactivity-api/1.0-alpha-4/plexus-interactivity-api-1.0-alpha-4.pom (7.1 kB at 158 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-monitor/2.0.6/maven-monitor-2.0.6.pom
Progress (1): 1.3 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-monitor/2.0.6/maven-monitor-2.0.6.pom (1.3 kB at 63 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/classworlds/classworlds/1.1/classworlds-1.1.pom
Progress (1): 2.2/3.3 kB
Progress (1): 3.3 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/classworlds/classworlds/1.1/classworlds-1.1.pom (3.3 kB at 119 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/2.0.5/plexus-utils-2.0.5.pom
Progress (1): 2.2/3.3 kB
Progress (1): 3.3 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/2.0.5/plexus-utils-2.0.5.pom (3.3 kB at 111 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/2.0.6/plexus-2.0.6.pom
Progress (1): 2.2/17 kB
Progress (1): 5.0/17 kB
Progress (1): 7.7/17 kB
Progress (1): 11/17 kB 
Progress (1): 13/17 kB
Progress (1): 16/17 kB
Progress (1): 17 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/2.0.6/plexus-2.0.6.pom (17 kB at 645 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-filtering/1.0/maven-filtering-1.0.pom
Progress (1): 2.0/5.8 kB
Progress (1): 4.8/5.8 kB
Progress (1): 5.8 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-filtering/1.0/maven-filtering-1.0.pom (5.8 kB at 192 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-shared-components/16/maven-shared-components-16.pom
Progress (1): 2.8/9.2 kB
Progress (1): 5.5/9.2 kB
Progress (1): 8.3/9.2 kB
Progress (1): 9.2 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-shared-components/16/maven-shared-components-16.pom (9.2 kB at 305 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/1.5.15/plexus-utils-1.5.15.pom
Progress (1): 2.2/6.8 kB
Progress (1): 5.0/6.8 kB
Progress (1): 6.8 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/1.5.15/plexus-utils-1.5.15.pom (6.8 kB at 274 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/2.0.2/plexus-2.0.2.pom
Progress (1): 2.8/12 kB
Progress (1): 5.5/12 kB
Progress (1): 8.3/12 kB
Progress (1): 11/12 kB 
Progress (1): 12 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/2.0.2/plexus-2.0.2.pom (12 kB at 447 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interpolation/1.12/plexus-interpolation-1.12.pom
Progress (1): 889 B
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interpolation/1.12/plexus-interpolation-1.12.pom (889 B at 36 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-components/1.1.14/plexus-components-1.1.14.pom
Progress (1): 2.2/5.8 kB
Progress (1): 5.0/5.8 kB
Progress (1): 5.8 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-components/1.1.14/plexus-components-1.1.14.pom (5.8 kB at 216 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/sonatype/plexus/plexus-build-api/0.0.4/plexus-build-api-0.0.4.pom
Progress (1): 2.2/2.9 kB
Progress (1): 2.9 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/sonatype/plexus/plexus-build-api/0.0.4/plexus-build-api-0.0.4.pom (2.9 kB at 82 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/sonatype/spice/spice-parent/10/spice-parent-10.pom
Progress (1): 2.8/3.0 kB
Progress (1): 3.0 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/sonatype/spice/spice-parent/10/spice-parent-10.pom (3.0 kB at 86 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/sonatype/forge/forge-parent/3/forge-parent-3.pom
Progress (1): 2.8/5.0 kB
Progress (1): 5.0 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/sonatype/forge/forge-parent/3/forge-parent-3.pom (5.0 kB at 129 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/1.5.8/plexus-utils-1.5.8.pom
Progress (1): 2.2/8.1 kB
Progress (1): 5.0/8.1 kB
Progress (1): 7.8/8.1 kB
Progress (1): 8.1 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/1.5.8/plexus-utils-1.5.8.pom (8.1 kB at 197 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interpolation/1.13/plexus-interpolation-1.13.pom
Progress (1): 890 B
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interpolation/1.13/plexus-interpolation-1.13.pom (890 B at 21 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-components/1.1.15/plexus-components-1.1.15.pom
Progress (1): 2.2/2.8 kB
Progress (1): 2.8 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-components/1.1.15/plexus-components-1.1.15.pom (2.8 kB at 92 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/2.0.3/plexus-2.0.3.pom
Progress (1): 2.2/15 kB
Progress (1): 5.0/15 kB
Progress (1): 7.8/15 kB
Progress (1): 11/15 kB 
Progress (1): 13/15 kB
Progress (1): 15 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/2.0.3/plexus-2.0.3.pom (15 kB at 418 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-project/2.0.6/maven-project-2.0.6.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-profile/2.0.6/maven-profile-2.0.6.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-registry/2.0.6/maven-plugin-registry-2.0.6.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact-manager/2.0.6/maven-artifact-manager-2.0.6.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-core/2.0.6/maven-core-2.0.6.jar
Progress (1): 2.2/116 kB
Progress (1): 5.0/116 kB
Progress (1): 7.7/116 kB
Progress (1): 10/116 kB 
Progress (2): 10/116 kB | 2.2/35 kB
Progress (2): 13/116 kB | 2.2/35 kB
Progress (2): 13/116 kB | 5.0/35 kB
Progress (2): 13/116 kB | 7.7/35 kB
Progress (2): 13/116 kB | 10/35 kB 
Progress (2): 13/116 kB | 13/35 kB
Progress (2): 13/116 kB | 16/35 kB
Progress (2): 13/116 kB | 19/35 kB
Progress (2): 13/116 kB | 21/35 kB
Progress (2): 13/116 kB | 24/35 kB
Progress (2): 13/116 kB | 27/35 kB
Progress (2): 13/116 kB | 30/35 kB
Progress (2): 13/116 kB | 32/35 kB
Progress (2): 13/116 kB | 35/35 kB
Progress (2): 13/116 kB | 35 kB   
Progress (3): 13/116 kB | 35 kB | 2.8/57 kB
Progress (3): 16/116 kB | 35 kB | 2.8/57 kB
Progress (3): 16/116 kB | 35 kB | 5.5/57 kB
Progress (3): 16/116 kB | 35 kB | 8.3/57 kB
Progress (3): 19/116 kB | 35 kB | 8.3/57 kB
Progress (3): 21/116 kB | 35 kB | 8.3/57 kB
Progress (4): 21/116 kB | 35 kB | 8.3/57 kB | 2.2/152 kB
Progress (4): 21/116 kB | 35 kB | 11/57 kB | 2.2/152 kB 
Progress (4): 21/116 kB | 35 kB | 14/57 kB | 2.2/152 kB
Progress (4): 21/116 kB | 35 kB | 16/57 kB | 2.2/152 kB
Progress (4): 21/116 kB | 35 kB | 19/57 kB | 2.2/152 kB
Progress (5): 21/116 kB | 35 kB | 19/57 kB | 2.2/152 kB | 2.2/29 kB
Progress (5): 21/116 kB | 35 kB | 19/57 kB | 2.2/152 kB | 5.0/29 kB
Progress (5): 21/116 kB | 35 kB | 19/57 kB | 2.2/152 kB | 7.7/29 kB
Progress (5): 21/116 kB | 35 kB | 19/57 kB | 2.2/152 kB | 10/29 kB 
Progress (5): 21/116 kB | 35 kB | 19/57 kB | 2.2/152 kB | 13/29 kB
Progress (5): 21/116 kB | 35 kB | 19/57 kB | 2.2/152 kB | 16/29 kB
Progress (5): 21/116 kB | 35 kB | 19/57 kB | 2.2/152 kB | 19/29 kB
Progress (5): 21/116 kB | 35 kB | 19/57 kB | 2.2/152 kB | 21/29 kB
Progress (5): 21/116 kB | 35 kB | 19/57 kB | 5.0/152 kB | 21/29 kB
                                                                  
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-profile/2.0.6/maven-profile-2.0.6.jar (35 kB at 323 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-parameter-documenter/2.0.6/maven-plugin-parameter-documenter-2.0.6.jar
Progress (4): 21/116 kB | 22/57 kB | 5.0/152 kB | 21/29 kB
Progress (4): 24/116 kB | 22/57 kB | 5.0/152 kB | 21/29 kB
Progress (4): 24/116 kB | 22/57 kB | 5.0/152 kB | 24/29 kB
Progress (4): 24/116 kB | 22/57 kB | 7.7/152 kB | 24/29 kB
Progress (4): 24/116 kB | 25/57 kB | 7.7/152 kB | 24/29 kB
Progress (4): 24/116 kB | 25/57 kB | 7.7/152 kB | 27/29 kB
Progress (4): 24/116 kB | 25/57 kB | 7.7/152 kB | 29 kB   
Progress (4): 24/116 kB | 27/57 kB | 7.7/152 kB | 29 kB
Progress (4): 27/116 kB | 27/57 kB | 7.7/152 kB | 29 kB
Progress (4): 30/116 kB | 27/57 kB | 7.7/152 kB | 29 kB
Progress (5): 30/116 kB | 27/57 kB | 7.7/152 kB | 29 kB | 2.2/21 kB
Progress (5): 30/116 kB | 27/57 kB | 7.7/152 kB | 29 kB | 5.0/21 kB
Progress (5): 30/116 kB | 27/57 kB | 7.7/152 kB | 29 kB | 7.7/21 kB
Progress (5): 32/116 kB | 27/57 kB | 7.7/152 kB | 29 kB | 7.7/21 kB
Progress (5): 32/116 kB | 27/57 kB | 10/152 kB | 29 kB | 7.7/21 kB 
Progress (5): 32/116 kB | 30/57 kB | 10/152 kB | 29 kB | 7.7/21 kB
Progress (5): 32/116 kB | 33/57 kB | 10/152 kB | 29 kB | 7.7/21 kB
Progress (5): 36/116 kB | 33/57 kB | 10/152 kB | 29 kB | 7.7/21 kB
Progress (5): 40/116 kB | 33/57 kB | 10/152 kB | 29 kB | 7.7/21 kB
Progress (5): 40/116 kB | 33/57 kB | 10/152 kB | 29 kB | 10/21 kB 
Progress (5): 45/116 kB | 33/57 kB | 10/152 kB | 29 kB | 10/21 kB
Progress (5): 49/116 kB | 33/57 kB | 10/152 kB | 29 kB | 10/21 kB
Progress (5): 49/116 kB | 37/57 kB | 10/152 kB | 29 kB | 10/21 kB
Progress (5): 49/116 kB | 37/57 kB | 13/152 kB | 29 kB | 10/21 kB
Progress (5): 49/116 kB | 41/57 kB | 13/152 kB | 29 kB | 10/21 kB
Progress (5): 53/116 kB | 41/57 kB | 13/152 kB | 29 kB | 10/21 kB
Progress (5): 53/116 kB | 41/57 kB | 13/152 kB | 29 kB | 13/21 kB
                                                                 
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-registry/2.0.6/maven-plugin-registry-2.0.6.jar (29 kB at 226 kB/s)
Progress (4): 57/116 kB | 41/57 kB | 13/152 kB | 13/21 kB
Progress (4): 57/116 kB | 45/57 kB | 13/152 kB | 13/21 kB
Progress (4): 57/116 kB | 45/57 kB | 16/152 kB | 13/21 kB
Progress (4): 57/116 kB | 49/57 kB | 16/152 kB | 13/21 kB
                                                         
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/reporting/maven-reporting-api/2.0.6/maven-reporting-api-2.0.6.jar
Progress (4): 61/116 kB | 49/57 kB | 16/152 kB | 13/21 kB
Progress (4): 65/116 kB | 49/57 kB | 16/152 kB | 13/21 kB
Progress (4): 69/116 kB | 49/57 kB | 16/152 kB | 13/21 kB
Progress (4): 73/116 kB | 49/57 kB | 16/152 kB | 13/21 kB
Progress (4): 77/116 kB | 49/57 kB | 16/152 kB | 13/21 kB
Progress (4): 81/116 kB | 49/57 kB | 16/152 kB | 13/21 kB
Progress (4): 81/116 kB | 49/57 kB | 16/152 kB | 16/21 kB
Progress (4): 81/116 kB | 53/57 kB | 16/152 kB | 16/21 kB
Progress (4): 81/116 kB | 53/57 kB | 19/152 kB | 16/21 kB
Progress (4): 81/116 kB | 53/57 kB | 19/152 kB | 19/21 kB
Progress (4): 81/116 kB | 53/57 kB | 21/152 kB | 19/21 kB
Progress (4): 81/116 kB | 57 kB | 21/152 kB | 19/21 kB   
Progress (4): 81/116 kB | 57 kB | 24/152 kB | 19/21 kB
Progress (4): 81/116 kB | 57 kB | 27/152 kB | 19/21 kB
Progress (4): 81/116 kB | 57 kB | 30/152 kB | 19/21 kB
Progress (4): 81/116 kB | 57 kB | 30/152 kB | 21 kB   
Progress (4): 81/116 kB | 57 kB | 32/152 kB | 21 kB
Progress (4): 85/116 kB | 57 kB | 32/152 kB | 21 kB
Progress (4): 90/116 kB | 57 kB | 32/152 kB | 21 kB
Progress (4): 94/116 kB | 57 kB | 32/152 kB | 21 kB
Progress (4): 98/116 kB | 57 kB | 32/152 kB | 21 kB
Progress (4): 98/116 kB | 57 kB | 36/152 kB | 21 kB
Progress (4): 98/116 kB | 57 kB | 40/152 kB | 21 kB
Progress (4): 98/116 kB | 57 kB | 45/152 kB | 21 kB
Progress (4): 102/116 kB | 57 kB | 45/152 kB | 21 kB
Progress (4): 102/116 kB | 57 kB | 49/152 kB | 21 kB
Progress (4): 106/116 kB | 57 kB | 49/152 kB | 21 kB
Progress (4): 106/116 kB | 57 kB | 53/152 kB | 21 kB
Progress (4): 110/116 kB | 57 kB | 53/152 kB | 21 kB
Progress (4): 114/116 kB | 57 kB | 53/152 kB | 21 kB
Progress (4): 114/116 kB | 57 kB | 57/152 kB | 21 kB
Progress (4): 114/116 kB | 57 kB | 61/152 kB | 21 kB
Progress (4): 114/116 kB | 57 kB | 65/152 kB | 21 kB
Progress (4): 114/116 kB | 57 kB | 69/152 kB | 21 kB
Progress (4): 114/116 kB | 57 kB | 73/152 kB | 21 kB
Progress (4): 114/116 kB | 57 kB | 77/152 kB | 21 kB
Progress (4): 114/116 kB | 57 kB | 81/152 kB | 21 kB
Progress (4): 114/116 kB | 57 kB | 85/152 kB | 21 kB
Progress (4): 114/116 kB | 57 kB | 90/152 kB | 21 kB
Progress (4): 114/116 kB | 57 kB | 94/152 kB | 21 kB
Progress (4): 114/116 kB | 57 kB | 98/152 kB | 21 kB
Progress (5): 114/116 kB | 57 kB | 98/152 kB | 21 kB | 2.2/9.9 kB
Progress (5): 116 kB | 57 kB | 98/152 kB | 21 kB | 2.2/9.9 kB    
Progress (5): 116 kB | 57 kB | 98/152 kB | 21 kB | 3.7/9.9 kB
Progress (5): 116 kB | 57 kB | 98/152 kB | 21 kB | 6.5/9.9 kB
Progress (5): 116 kB | 57 kB | 98/152 kB | 21 kB | 9.2/9.9 kB
Progress (5): 116 kB | 57 kB | 98/152 kB | 21 kB | 9.9 kB    
                                                         
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact-manager/2.0.6/maven-artifact-manager-2.0.6.jar (57 kB at 330 kB/s)
Progress (4): 116 kB | 102/152 kB | 21 kB | 9.9 kB
                                                  
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/doxia/doxia-sink-api/1.0-alpha-7/doxia-sink-api-1.0-alpha-7.jar
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-parameter-documenter/2.0.6/maven-plugin-parameter-documenter-2.0.6.jar (21 kB at 124 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-repository-metadata/2.0.6/maven-repository-metadata-2.0.6.jar
Progress (3): 116 kB | 106/152 kB | 9.9 kB
Progress (3): 116 kB | 110/152 kB | 9.9 kB
Progress (3): 116 kB | 114/152 kB | 9.9 kB
Progress (3): 116 kB | 118/152 kB | 9.9 kB
Progress (3): 116 kB | 122/152 kB | 9.9 kB
Progress (3): 116 kB | 126/152 kB | 9.9 kB
Progress (3): 116 kB | 131/152 kB | 9.9 kB
Progress (3): 116 kB | 135/152 kB | 9.9 kB
Progress (3): 116 kB | 139/152 kB | 9.9 kB
Progress (3): 116 kB | 143/152 kB | 9.9 kB
Progress (3): 116 kB | 147/152 kB | 9.9 kB
Progress (3): 116 kB | 151/152 kB | 9.9 kB
Progress (3): 116 kB | 152 kB | 9.9 kB    
Progress (4): 116 kB | 152 kB | 9.9 kB | 2.2/24 kB
                                                  
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-project/2.0.6/maven-project-2.0.6.jar (116 kB at 551 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-error-diagnostics/2.0.6/maven-error-diagnostics-2.0.6.jar
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/reporting/maven-reporting-api/2.0.6/maven-reporting-api-2.0.6.jar (9.9 kB at 51 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/commons-cli/commons-cli/1.0/commons-cli-1.0.jar
Progress (2): 152 kB | 5.0/24 kB
Progress (2): 152 kB | 7.7/24 kB
Progress (2): 152 kB | 10/24 kB 
Progress (2): 152 kB | 13/24 kB
Progress (2): 152 kB | 16/24 kB
Progress (2): 152 kB | 19/24 kB
Progress (2): 152 kB | 21/24 kB
                               
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-core/2.0.6/maven-core-2.0.6.jar (152 kB at 736 kB/s)
Progress (2): 21/24 kB | 4.1/5.9 kB
                                   
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-descriptor/2.0.6/maven-plugin-descriptor-2.0.6.jar
Progress (2): 24/24 kB | 4.1/5.9 kB
Progress (2): 24/24 kB | 5.9 kB    
Progress (3): 24/24 kB | 5.9 kB | 2.2/30 kB
Progress (3): 24/24 kB | 5.9 kB | 5.0/30 kB
Progress (3): 24 kB | 5.9 kB | 5.0/30 kB   
Progress (4): 24 kB | 5.9 kB | 5.0/30 kB | 2.8/14 kB
Progress (4): 24 kB | 5.9 kB | 7.7/30 kB | 2.8/14 kB
Progress (4): 24 kB | 5.9 kB | 10/30 kB | 2.8/14 kB 
Progress (4): 24 kB | 5.9 kB | 10/30 kB | 5.5/14 kB
Progress (4): 24 kB | 5.9 kB | 13/30 kB | 5.5/14 kB
Progress (4): 24 kB | 5.9 kB | 16/30 kB | 5.5/14 kB
Progress (4): 24 kB | 5.9 kB | 16/30 kB | 8.3/14 kB
Progress (4): 24 kB | 5.9 kB | 19/30 kB | 8.3/14 kB
Progress (4): 24 kB | 5.9 kB | 19/30 kB | 11/14 kB 
Progress (4): 24 kB | 5.9 kB | 21/30 kB | 11/14 kB
Progress (4): 24 kB | 5.9 kB | 21/30 kB | 14 kB   
Progress (4): 24 kB | 5.9 kB | 24/30 kB | 14 kB
Progress (4): 24 kB | 5.9 kB | 27/30 kB | 14 kB
Progress (4): 24 kB | 5.9 kB | 30/30 kB | 14 kB
Progress (4): 24 kB | 5.9 kB | 30 kB | 14 kB   
Progress (5): 24 kB | 5.9 kB | 30 kB | 14 kB | 2.2/37 kB
                                                        
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/doxia/doxia-sink-api/1.0-alpha-7/doxia-sink-api-1.0-alpha-7.jar (5.9 kB at 26 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interactivity-api/1.0-alpha-4/plexus-interactivity-api-1.0-alpha-4.jar
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-repository-metadata/2.0.6/maven-repository-metadata-2.0.6.jar (24 kB at 105 kB/s)
Progress (3): 30 kB | 14 kB | 5.0/37 kB
Progress (3): 30 kB | 14 kB | 7.7/37 kB
                                       
Downloading from central: https://repo.maven.apache.org/maven2/classworlds/classworlds/1.1/classworlds-1.1.jar
Progress (3): 30 kB | 14 kB | 10/37 kB
Progress (3): 30 kB | 14 kB | 13/37 kB
Progress (3): 30 kB | 14 kB | 16/37 kB
Progress (3): 30 kB | 14 kB | 19/37 kB
                                      
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-error-diagnostics/2.0.6/maven-error-diagnostics-2.0.6.jar (14 kB at 57 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact/2.0.6/maven-artifact-2.0.6.jar
Downloaded from central: https://repo.maven.apache.org/maven2/commons-cli/commons-cli/1.0/commons-cli-1.0.jar (30 kB at 124 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-settings/2.0.6/maven-settings-2.0.6.jar
Progress (2): 19/37 kB | 2.2/38 kB
Progress (2): 21/37 kB | 2.2/38 kB
Progress (2): 24/37 kB | 2.2/38 kB
Progress (2): 27/37 kB | 2.2/38 kB
Progress (2): 30/37 kB | 2.2/38 kB
Progress (2): 32/37 kB | 2.2/38 kB
Progress (2): 35/37 kB | 2.2/38 kB
Progress (2): 37 kB | 2.2/38 kB   
Progress (3): 37 kB | 2.2/38 kB | 2.2/13 kB
Progress (3): 37 kB | 2.2/38 kB | 5.0/13 kB
Progress (3): 37 kB | 2.2/38 kB | 7.7/13 kB
Progress (3): 37 kB | 2.2/38 kB | 10/13 kB 
Progress (3): 37 kB | 2.2/38 kB | 13/13 kB
Progress (3): 37 kB | 2.2/38 kB | 13 kB   
Progress (3): 37 kB | 5.0/38 kB | 13 kB
Progress (3): 37 kB | 7.7/38 kB | 13 kB
Progress (3): 37 kB | 10/38 kB | 13 kB 
Progress (3): 37 kB | 13/38 kB | 13 kB
Progress (3): 37 kB | 16/38 kB | 13 kB
Progress (3): 37 kB | 19/38 kB | 13 kB
Progress (3): 37 kB | 21/38 kB | 13 kB
Progress (3): 37 kB | 24/38 kB | 13 kB
Progress (3): 37 kB | 27/38 kB | 13 kB
Progress (3): 37 kB | 30/38 kB | 13 kB
Progress (3): 37 kB | 32/38 kB | 13 kB
Progress (3): 37 kB | 35/38 kB | 13 kB
Progress (4): 37 kB | 35/38 kB | 13 kB | 2.2/87 kB
Progress (4): 37 kB | 35/38 kB | 13 kB | 5.0/87 kB
Progress (4): 37 kB | 38/38 kB | 13 kB | 5.0/87 kB
Progress (4): 37 kB | 38 kB | 13 kB | 5.0/87 kB   
Progress (4): 37 kB | 38 kB | 13 kB | 7.7/87 kB
                                               
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-descriptor/2.0.6/maven-plugin-descriptor-2.0.6.jar (37 kB at 137 kB/s)
Progress (3): 38 kB | 13 kB | 10/87 kB
Progress (4): 38 kB | 13 kB | 10/87 kB | 2.2/49 kB
                                                  
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interactivity-api/1.0-alpha-4/plexus-interactivity-api-1.0-alpha-4.jar (13 kB at 50 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-monitor/2.0.6/maven-monitor-2.0.6.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-model/2.0.6/maven-model-2.0.6.jar
Progress (3): 38 kB | 10/87 kB | 5.0/49 kB
Progress (3): 38 kB | 10/87 kB | 7.7/49 kB
Progress (3): 38 kB | 13/87 kB | 7.7/49 kB
Progress (3): 38 kB | 13/87 kB | 10/49 kB 
Progress (3): 38 kB | 13/87 kB | 13/49 kB
Progress (3): 38 kB | 13/87 kB | 16/49 kB
                                         
Downloaded from central: https://repo.maven.apache.org/maven2/classworlds/classworlds/1.1/classworlds-1.1.jar (38 kB at 134 kB/s)
Progress (2): 13/87 kB | 19/49 kB
Progress (3): 13/87 kB | 19/49 kB | 2.2/10 kB
Progress (3): 13/87 kB | 21/49 kB | 2.2/10 kB
Progress (3): 13/87 kB | 21/49 kB | 5.0/10 kB
Progress (3): 13/87 kB | 21/49 kB | 7.7/10 kB
Progress (3): 13/87 kB | 21/49 kB | 10 kB    
Progress (4): 13/87 kB | 21/49 kB | 10 kB | 2.8/86 kB
Progress (4): 16/87 kB | 21/49 kB | 10 kB | 2.8/86 kB
Progress (4): 19/87 kB | 21/49 kB | 10 kB | 2.8/86 kB
Progress (4): 21/87 kB | 21/49 kB | 10 kB | 2.8/86 kB
Progress (4): 24/87 kB | 21/49 kB | 10 kB | 2.8/86 kB
Progress (4): 24/87 kB | 21/49 kB | 10 kB | 5.5/86 kB
Progress (4): 24/87 kB | 21/49 kB | 10 kB | 8.3/86 kB
Progress (4): 24/87 kB | 21/49 kB | 10 kB | 11/86 kB 
Progress (4): 24/87 kB | 21/49 kB | 10 kB | 14/86 kB
Progress (4): 24/87 kB | 21/49 kB | 10 kB | 16/86 kB
Progress (4): 24/87 kB | 21/49 kB | 10 kB | 19/86 kB
Progress (4): 24/87 kB | 21/49 kB | 10 kB | 22/86 kB
Progress (4): 24/87 kB | 21/49 kB | 10 kB | 25/86 kB
Progress (4): 24/87 kB | 21/49 kB | 10 kB | 27/86 kB
Progress (4): 24/87 kB | 21/49 kB | 10 kB | 30/86 kB
Progress (4): 24/87 kB | 21/49 kB | 10 kB | 33/86 kB
Progress (4): 24/87 kB | 21/49 kB | 10 kB | 37/86 kB
Progress (4): 24/87 kB | 21/49 kB | 10 kB | 41/86 kB
Progress (4): 24/87 kB | 24/49 kB | 10 kB | 41/86 kB
                                                    
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-container-default/1.0-alpha-9-stable-1/plexus-container-default-1.0-alpha-9-stable-1.jar
Progress (4): 24/87 kB | 24/49 kB | 10 kB | 45/86 kB
Progress (4): 24/87 kB | 27/49 kB | 10 kB | 45/86 kB
                                                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-monitor/2.0.6/maven-monitor-2.0.6.jar (10 kB at 34 kB/s)
Progress (3): 27/87 kB | 27/49 kB | 45/86 kB
                                            
Downloading from central: https://repo.maven.apache.org/maven2/junit/junit/3.8.1/junit-3.8.1.jar
Progress (3): 27/87 kB | 27/49 kB | 49/86 kB
Progress (3): 27/87 kB | 30/49 kB | 49/86 kB
Progress (3): 30/87 kB | 30/49 kB | 49/86 kB
Progress (3): 30/87 kB | 32/49 kB | 49/86 kB
Progress (3): 32/87 kB | 32/49 kB | 49/86 kB
Progress (3): 32/87 kB | 36/49 kB | 49/86 kB
Progress (3): 32/87 kB | 40/49 kB | 49/86 kB
Progress (3): 32/87 kB | 45/49 kB | 49/86 kB
Progress (3): 32/87 kB | 49/49 kB | 49/86 kB
Progress (3): 32/87 kB | 49 kB | 49/86 kB   
Progress (4): 32/87 kB | 49 kB | 49/86 kB | 2.8/194 kB
Progress (4): 36/87 kB | 49 kB | 49/86 kB | 2.8/194 kB
Progress (4): 40/87 kB | 49 kB | 49/86 kB | 2.8/194 kB
Progress (4): 45/87 kB | 49 kB | 49/86 kB | 2.8/194 kB
Progress (4): 45/87 kB | 49 kB | 49/86 kB | 5.5/194 kB
Progress (4): 49/87 kB | 49 kB | 49/86 kB | 5.5/194 kB
Progress (4): 53/87 kB | 49 kB | 49/86 kB | 5.5/194 kB
Progress (4): 57/87 kB | 49 kB | 49/86 kB | 5.5/194 kB
Progress (4): 61/87 kB | 49 kB | 49/86 kB | 5.5/194 kB
Progress (4): 65/87 kB | 49 kB | 49/86 kB | 5.5/194 kB
Progress (4): 69/87 kB | 49 kB | 49/86 kB | 5.5/194 kB
Progress (4): 73/87 kB | 49 kB | 49/86 kB | 5.5/194 kB
Progress (4): 77/87 kB | 49 kB | 49/86 kB | 5.5/194 kB
Progress (4): 81/87 kB | 49 kB | 49/86 kB | 5.5/194 kB
Progress (4): 85/87 kB | 49 kB | 49/86 kB | 5.5/194 kB
Progress (4): 87 kB | 49 kB | 49/86 kB | 5.5/194 kB   
Progress (4): 87 kB | 49 kB | 53/86 kB | 5.5/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 8.3/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 11/194 kB 
Progress (4): 87 kB | 49 kB | 53/86 kB | 14/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 16/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 19/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 22/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 25/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 27/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 30/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 33/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 37/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 41/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 45/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 49/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 53/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 57/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 61/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 66/194 kB
Progress (4): 87 kB | 49 kB | 53/86 kB | 70/194 kB
                                                  
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-settings/2.0.6/maven-settings-2.0.6.jar (49 kB at 151 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/2.0.5/plexus-utils-2.0.5.jar
Progress (3): 87 kB | 57/86 kB | 70/194 kB
Progress (3): 87 kB | 61/86 kB | 70/194 kB
Progress (3): 87 kB | 66/86 kB | 70/194 kB
Progress (3): 87 kB | 70/86 kB | 70/194 kB
Progress (3): 87 kB | 74/86 kB | 70/194 kB
Progress (3): 87 kB | 78/86 kB | 70/194 kB
Progress (3): 87 kB | 82/86 kB | 70/194 kB
Progress (3): 87 kB | 86/86 kB | 70/194 kB
Progress (3): 87 kB | 86 kB | 70/194 kB   
Progress (4): 87 kB | 86 kB | 70/194 kB | 2.2/121 kB
                                                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact/2.0.6/maven-artifact-2.0.6.jar (87 kB at 258 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-filtering/1.0/maven-filtering-1.0.jar
Progress (3): 86 kB | 74/194 kB | 2.2/121 kB
Progress (4): 86 kB | 74/194 kB | 2.2/121 kB | 4.1/223 kB
Progress (4): 86 kB | 74/194 kB | 2.2/121 kB | 7.7/223 kB
Progress (4): 86 kB | 74/194 kB | 5.0/121 kB | 7.7/223 kB
Progress (4): 86 kB | 74/194 kB | 5.0/121 kB | 12/223 kB 
Progress (4): 86 kB | 74/194 kB | 5.0/121 kB | 16/223 kB
Progress (4): 86 kB | 78/194 kB | 5.0/121 kB | 16/223 kB
Progress (4): 86 kB | 78/194 kB | 7.7/121 kB | 16/223 kB
Progress (4): 86 kB | 78/194 kB | 10/121 kB | 16/223 kB 
Progress (4): 86 kB | 78/194 kB | 10/121 kB | 20/223 kB
Progress (4): 86 kB | 78/194 kB | 10/121 kB | 24/223 kB
Progress (4): 86 kB | 78/194 kB | 10/121 kB | 28/223 kB
Progress (4): 86 kB | 78/194 kB | 10/121 kB | 32/223 kB
Progress (4): 86 kB | 78/194 kB | 13/121 kB | 32/223 kB
Progress (4): 86 kB | 78/194 kB | 16/121 kB | 32/223 kB
Progress (4): 86 kB | 78/194 kB | 16/121 kB | 36/223 kB
Progress (4): 86 kB | 78/194 kB | 16/121 kB | 40/223 kB
Progress (4): 86 kB | 82/194 kB | 16/121 kB | 40/223 kB
Progress (4): 86 kB | 86/194 kB | 16/121 kB | 40/223 kB
Progress (4): 86 kB | 90/194 kB | 16/121 kB | 40/223 kB
Progress (4): 86 kB | 94/194 kB | 16/121 kB | 40/223 kB
Progress (4): 86 kB | 98/194 kB | 16/121 kB | 40/223 kB
Progress (4): 86 kB | 98/194 kB | 16/121 kB | 45/223 kB
Progress (4): 86 kB | 98/194 kB | 19/121 kB | 45/223 kB
Progress (4): 86 kB | 98/194 kB | 19/121 kB | 49/223 kB
Progress (5): 86 kB | 98/194 kB | 19/121 kB | 49/223 kB | 2.8/43 kB
Progress (5): 86 kB | 98/194 kB | 19/121 kB | 53/223 kB | 2.8/43 kB
Progress (5): 86 kB | 98/194 kB | 19/121 kB | 57/223 kB | 2.8/43 kB
Progress (5): 86 kB | 98/194 kB | 19/121 kB | 61/223 kB | 2.8/43 kB
Progress (5): 86 kB | 98/194 kB | 19/121 kB | 65/223 kB | 2.8/43 kB
Progress (5): 86 kB | 98/194 kB | 19/121 kB | 69/223 kB | 2.8/43 kB
Progress (5): 86 kB | 98/194 kB | 19/121 kB | 73/223 kB | 2.8/43 kB
Progress (5): 86 kB | 98/194 kB | 19/121 kB | 77/223 kB | 2.8/43 kB
Progress (5): 86 kB | 98/194 kB | 19/121 kB | 81/223 kB | 2.8/43 kB
Progress (5): 86 kB | 98/194 kB | 19/121 kB | 85/223 kB | 2.8/43 kB
Progress (5): 86 kB | 98/194 kB | 19/121 kB | 90/223 kB | 2.8/43 kB
Progress (5): 86 kB | 98/194 kB | 19/121 kB | 94/223 kB | 2.8/43 kB
Progress (5): 86 kB | 98/194 kB | 19/121 kB | 98/223 kB | 2.8/43 kB
Progress (5): 86 kB | 102/194 kB | 19/121 kB | 98/223 kB | 2.8/43 kB
Progress (5): 86 kB | 106/194 kB | 19/121 kB | 98/223 kB | 2.8/43 kB
                                                                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-model/2.0.6/maven-model-2.0.6.jar (86 kB at 232 kB/s)
Progress (4): 111/194 kB | 19/121 kB | 98/223 kB | 2.8/43 kB
Progress (4): 111/194 kB | 19/121 kB | 102/223 kB | 2.8/43 kB
Progress (4): 111/194 kB | 19/121 kB | 102/223 kB | 5.5/43 kB
Progress (4): 111/194 kB | 21/121 kB | 102/223 kB | 5.5/43 kB
Progress (4): 111/194 kB | 21/121 kB | 102/223 kB | 8.3/43 kB
Progress (4): 111/194 kB | 21/121 kB | 106/223 kB | 8.3/43 kB
Progress (4): 115/194 kB | 21/121 kB | 106/223 kB | 8.3/43 kB
                                                             
Downloading from central: https://repo.maven.apache.org/maven2/org/sonatype/plexus/plexus-build-api/0.0.4/plexus-build-api-0.0.4.jar
Progress (4): 119/194 kB | 21/121 kB | 106/223 kB | 8.3/43 kB
Progress (4): 123/194 kB | 21/121 kB | 106/223 kB | 8.3/43 kB
Progress (4): 127/194 kB | 21/121 kB | 106/223 kB | 8.3/43 kB
Progress (4): 127/194 kB | 21/121 kB | 110/223 kB | 8.3/43 kB
Progress (4): 127/194 kB | 21/121 kB | 110/223 kB | 11/43 kB 
Progress (4): 127/194 kB | 24/121 kB | 110/223 kB | 11/43 kB
Progress (4): 127/194 kB | 24/121 kB | 110/223 kB | 14/43 kB
Progress (4): 127/194 kB | 24/121 kB | 114/223 kB | 14/43 kB
Progress (4): 131/194 kB | 24/121 kB | 114/223 kB | 14/43 kB
Progress (4): 131/194 kB | 24/121 kB | 114/223 kB | 16/43 kB
Progress (4): 131/194 kB | 27/121 kB | 114/223 kB | 16/43 kB
Progress (4): 131/194 kB | 27/121 kB | 114/223 kB | 19/43 kB
Progress (4): 131/194 kB | 27/121 kB | 118/223 kB | 19/43 kB
Progress (4): 135/194 kB | 27/121 kB | 118/223 kB | 19/43 kB
Progress (4): 135/194 kB | 30/121 kB | 118/223 kB | 19/43 kB
Progress (4): 135/194 kB | 30/121 kB | 118/223 kB | 22/43 kB
Progress (4): 135/194 kB | 30/121 kB | 122/223 kB | 22/43 kB
Progress (4): 135/194 kB | 30/121 kB | 122/223 kB | 25/43 kB
Progress (5): 135/194 kB | 30/121 kB | 122/223 kB | 25/43 kB | 2.2/6.8 kB
Progress (5): 135/194 kB | 30/121 kB | 122/223 kB | 25/43 kB | 5.0/6.8 kB
Progress (5): 135/194 kB | 30/121 kB | 122/223 kB | 25/43 kB | 6.8 kB    
Progress (5): 139/194 kB | 30/121 kB | 122/223 kB | 25/43 kB | 6.8 kB
Progress (5): 139/194 kB | 32/121 kB | 122/223 kB | 25/43 kB | 6.8 kB
Progress (5): 139/194 kB | 32/121 kB | 126/223 kB | 25/43 kB | 6.8 kB
Progress (5): 139/194 kB | 32/121 kB | 131/223 kB | 25/43 kB | 6.8 kB
Progress (5): 139/194 kB | 32/121 kB | 131/223 kB | 27/43 kB | 6.8 kB
Progress (5): 139/194 kB | 32/121 kB | 131/223 kB | 30/43 kB | 6.8 kB
Progress (5): 139/194 kB | 32/121 kB | 131/223 kB | 33/43 kB | 6.8 kB
Progress (5): 139/194 kB | 32/121 kB | 131/223 kB | 37/43 kB | 6.8 kB
Progress (5): 139/194 kB | 32/121 kB | 135/223 kB | 37/43 kB | 6.8 kB
Progress (5): 139/194 kB | 32/121 kB | 139/223 kB | 37/43 kB | 6.8 kB
Progress (5): 139/194 kB | 32/121 kB | 143/223 kB | 37/43 kB | 6.8 kB
Progress (5): 139/194 kB | 32/121 kB | 147/223 kB | 37/43 kB | 6.8 kB
Progress (5): 139/194 kB | 36/121 kB | 147/223 kB | 37/43 kB | 6.8 kB
Progress (5): 139/194 kB | 40/121 kB | 147/223 kB | 37/43 kB | 6.8 kB
Progress (5): 139/194 kB | 45/121 kB | 147/223 kB | 37/43 kB | 6.8 kB
Progress (5): 139/194 kB | 45/121 kB | 147/223 kB | 41/43 kB | 6.8 kB
Progress (5): 139/194 kB | 45/121 kB | 147/223 kB | 43 kB | 6.8 kB   
Progress (5): 139/194 kB | 49/121 kB | 147/223 kB | 43 kB | 6.8 kB
                                                                  
Downloaded from central: https://repo.maven.apache.org/maven2/org/sonatype/plexus/plexus-build-api/0.0.4/plexus-build-api-0.0.4.jar (6.8 kB at 17 kB/s)
Progress (4): 143/194 kB | 49/121 kB | 147/223 kB | 43 kB
Progress (4): 143/194 kB | 49/121 kB | 151/223 kB | 43 kB
Progress (4): 147/194 kB | 49/121 kB | 151/223 kB | 43 kB
                                                         
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interpolation/1.13/plexus-interpolation-1.13.jar
Progress (4): 152/194 kB | 49/121 kB | 151/223 kB | 43 kB
Progress (4): 156/194 kB | 49/121 kB | 151/223 kB | 43 kB
Progress (4): 160/194 kB | 49/121 kB | 151/223 kB | 43 kB
Progress (4): 164/194 kB | 49/121 kB | 151/223 kB | 43 kB
Progress (4): 168/194 kB | 49/121 kB | 151/223 kB | 43 kB
Progress (4): 172/194 kB | 49/121 kB | 151/223 kB | 43 kB
Progress (4): 176/194 kB | 49/121 kB | 151/223 kB | 43 kB
Progress (4): 180/194 kB | 49/121 kB | 151/223 kB | 43 kB
Progress (4): 180/194 kB | 53/121 kB | 151/223 kB | 43 kB
Progress (4): 180/194 kB | 53/121 kB | 155/223 kB | 43 kB
Progress (4): 180/194 kB | 57/121 kB | 155/223 kB | 43 kB
Progress (4): 180/194 kB | 61/121 kB | 155/223 kB | 43 kB
Progress (4): 180/194 kB | 65/121 kB | 155/223 kB | 43 kB
Progress (4): 180/194 kB | 65/121 kB | 159/223 kB | 43 kB
Progress (4): 180/194 kB | 65/121 kB | 163/223 kB | 43 kB
Progress (4): 180/194 kB | 69/121 kB | 163/223 kB | 43 kB
Progress (4): 180/194 kB | 73/121 kB | 163/223 kB | 43 kB
Progress (4): 180/194 kB | 77/121 kB | 163/223 kB | 43 kB
Progress (4): 180/194 kB | 81/121 kB | 163/223 kB | 43 kB
Progress (4): 180/194 kB | 81/121 kB | 167/223 kB | 43 kB
Progress (4): 180/194 kB | 81/121 kB | 171/223 kB | 43 kB
Progress (4): 184/194 kB | 81/121 kB | 171/223 kB | 43 kB
Progress (4): 188/194 kB | 81/121 kB | 171/223 kB | 43 kB
Progress (4): 193/194 kB | 81/121 kB | 171/223 kB | 43 kB
Progress (4): 193/194 kB | 81/121 kB | 176/223 kB | 43 kB
Progress (4): 193/194 kB | 85/121 kB | 176/223 kB | 43 kB
Progress (4): 193/194 kB | 85/121 kB | 180/223 kB | 43 kB
Progress (4): 194 kB | 85/121 kB | 180/223 kB | 43 kB    
                                                     
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-filtering/1.0/maven-filtering-1.0.jar (43 kB at 100 kB/s)
Progress (3): 194 kB | 90/121 kB | 180/223 kB
Progress (3): 194 kB | 90/121 kB | 184/223 kB
Progress (4): 194 kB | 90/121 kB | 184/223 kB | 2.8/61 kB
Progress (4): 194 kB | 94/121 kB | 184/223 kB | 2.8/61 kB
Progress (4): 194 kB | 98/121 kB | 184/223 kB | 2.8/61 kB
Progress (4): 194 kB | 98/121 kB | 184/223 kB | 5.5/61 kB
Progress (4): 194 kB | 102/121 kB | 184/223 kB | 5.5/61 kB
Progress (4): 194 kB | 102/121 kB | 188/223 kB | 5.5/61 kB
Progress (4): 194 kB | 102/121 kB | 188/223 kB | 8.3/61 kB
Progress (4): 194 kB | 106/121 kB | 188/223 kB | 8.3/61 kB
Progress (4): 194 kB | 106/121 kB | 188/223 kB | 11/61 kB 
Progress (4): 194 kB | 110/121 kB | 188/223 kB | 11/61 kB
Progress (4): 194 kB | 114/121 kB | 188/223 kB | 11/61 kB
Progress (4): 194 kB | 114/121 kB | 188/223 kB | 14/61 kB
Progress (4): 194 kB | 114/121 kB | 188/223 kB | 16/61 kB
Progress (4): 194 kB | 118/121 kB | 188/223 kB | 16/61 kB
Progress (4): 194 kB | 118/121 kB | 192/223 kB | 16/61 kB
Progress (4): 194 kB | 118/121 kB | 192/223 kB | 19/61 kB
                                                         
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-container-default/1.0-alpha-9-stable-1/plexus-container-default-1.0-alpha-9-stable-1.jar (194 kB at 435 kB/s)
Progress (3): 121 kB | 192/223 kB | 19/61 kB
Progress (3): 121 kB | 196/223 kB | 19/61 kB
Progress (3): 121 kB | 196/223 kB | 22/61 kB
Progress (3): 121 kB | 196/223 kB | 25/61 kB
Progress (3): 121 kB | 196/223 kB | 27/61 kB
Progress (3): 121 kB | 196/223 kB | 30/61 kB
Progress (3): 121 kB | 196/223 kB | 33/61 kB
Progress (3): 121 kB | 196/223 kB | 37/61 kB
Progress (3): 121 kB | 200/223 kB | 37/61 kB
Progress (3): 121 kB | 204/223 kB | 37/61 kB
Progress (3): 121 kB | 208/223 kB | 37/61 kB
Progress (3): 121 kB | 212/223 kB | 37/61 kB
Progress (3): 121 kB | 217/223 kB | 37/61 kB
Progress (3): 121 kB | 221/223 kB | 37/61 kB
Progress (3): 121 kB | 223 kB | 37/61 kB    
Progress (3): 121 kB | 223 kB | 41/61 kB
Progress (3): 121 kB | 223 kB | 45/61 kB
                                        
Downloaded from central: https://repo.maven.apache.org/maven2/junit/junit/3.8.1/junit-3.8.1.jar (121 kB at 264 kB/s)
Progress (2): 223 kB | 49/61 kB
Progress (2): 223 kB | 53/61 kB
Progress (2): 223 kB | 57/61 kB
Progress (2): 223 kB | 61 kB   
                            
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/2.0.5/plexus-utils-2.0.5.jar (223 kB at 477 kB/s)
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interpolation/1.13/plexus-interpolation-1.13.jar (61 kB at 130 kB/s)
[debug] execute contextualize
[INFO] Using 'utf-8' encoding to copy filtered resources.
[INFO] skip non existing resourceDirectory /home/jenkins/workspace/First_Pipeline/server/src/main/resources
[INFO] 
[INFO] --- maven-compiler-plugin:2.3.2:compile (default-compile) @ server ---
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-toolchain/1.0/maven-toolchain-1.0.pom
Progress (1): 2.8/3.4 kB
Progress (1): 3.4 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-toolchain/1.0/maven-toolchain-1.0.pom (3.4 kB at 78 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compiler-api/1.8.1/plexus-compiler-api-1.8.1.pom
Progress (1): 805 B
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compiler-api/1.8.1/plexus-compiler-api-1.8.1.pom (805 B at 28 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compiler/1.8.1/plexus-compiler-1.8.1.pom
Progress (1): 2.2/3.5 kB
Progress (1): 3.5 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compiler/1.8.1/plexus-compiler-1.8.1.pom (3.5 kB at 139 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-components/1.1.18/plexus-components-1.1.18.pom
Progress (1): 2.2/5.4 kB
Progress (1): 5.0/5.4 kB
Progress (1): 5.4 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-components/1.1.18/plexus-components-1.1.18.pom (5.4 kB at 185 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/2.0.7/plexus-2.0.7.pom
Progress (1): 2.2/17 kB
Progress (1): 5.0/17 kB
Progress (1): 7.7/17 kB
Progress (1): 11/17 kB 
Progress (1): 13/17 kB
Progress (1): 16/17 kB
Progress (1): 17 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/2.0.7/plexus-2.0.7.pom (17 kB at 467 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/1.5.5/plexus-utils-1.5.5.pom
Progress (1): 2.8/5.1 kB
Progress (1): 5.1 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/1.5.5/plexus-utils-1.5.5.pom (5.1 kB at 198 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compiler-manager/1.8.1/plexus-compiler-manager-1.8.1.pom
Progress (1): 713 B
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compiler-manager/1.8.1/plexus-compiler-manager-1.8.1.pom (713 B at 26 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compiler-javac/1.8.1/plexus-compiler-javac-1.8.1.pom
Progress (1): 710 B
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compiler-javac/1.8.1/plexus-compiler-javac-1.8.1.pom (710 B at 25 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compilers/1.8.1/plexus-compilers-1.8.1.pom
Progress (1): 1.3 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compilers/1.8.1/plexus-compilers-1.8.1.pom (1.3 kB at 46 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-toolchain/1.0/maven-toolchain-1.0.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compiler-api/1.8.1/plexus-compiler-api-1.8.1.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compiler-manager/1.8.1/plexus-compiler-manager-1.8.1.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compiler-javac/1.8.1/plexus-compiler-javac-1.8.1.jar
Progress (1): 2.8/33 kB
Progress (1): 5.5/33 kB
Progress (1): 8.3/33 kB
Progress (1): 11/33 kB 
Progress (1): 14/33 kB
Progress (1): 16/33 kB
Progress (1): 19/33 kB
Progress (1): 22/33 kB
Progress (1): 25/33 kB
Progress (1): 27/33 kB
Progress (1): 30/33 kB
Progress (1): 33/33 kB
Progress (1): 33 kB   
Progress (2): 33 kB | 4.1/20 kB
Progress (3): 33 kB | 4.1/20 kB | 2.2/5.3 kB
Progress (3): 33 kB | 7.7/20 kB | 2.2/5.3 kB
Progress (3): 33 kB | 12/20 kB | 2.2/5.3 kB 
Progress (3): 33 kB | 16/20 kB | 2.2/5.3 kB
Progress (3): 33 kB | 20/20 kB | 2.2/5.3 kB
Progress (3): 33 kB | 20 kB | 2.2/5.3 kB   
Progress (3): 33 kB | 20 kB | 5.0/5.3 kB
Progress (3): 33 kB | 20 kB | 5.3 kB    
                                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-toolchain/1.0/maven-toolchain-1.0.jar (33 kB at 997 kB/s)
Progress (3): 20 kB | 5.3 kB | 4.1/13 kB
Progress (3): 20 kB | 5.3 kB | 7.7/13 kB
Progress (3): 20 kB | 5.3 kB | 12/13 kB 
Progress (3): 20 kB | 5.3 kB | 13 kB   
                                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compiler-api/1.8.1/plexus-compiler-api-1.8.1.jar (20 kB at 644 kB/s)
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compiler-manager/1.8.1/plexus-compiler-manager-1.8.1.jar (5.3 kB at 157 kB/s)
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-compiler-javac/1.8.1/plexus-compiler-javac-1.8.1.jar (13 kB at 305 kB/s)
[INFO] Compiling 2 source files to /home/jenkins/workspace/First_Pipeline/server/target/classes
[INFO] 
[INFO] --- maven-resources-plugin:2.5:testResources (default-testResources) @ server ---
[debug] execute contextualize
[INFO] Using 'utf-8' encoding to copy filtered resources.
[INFO] skip non existing resourceDirectory /home/jenkins/workspace/First_Pipeline/server/src/test/resources
[INFO] 
[INFO] --- maven-compiler-plugin:2.3.2:testCompile (default-testCompile) @ server ---
[INFO] Compiling 1 source file to /home/jenkins/workspace/First_Pipeline/server/target/test-classes
[INFO] 
[INFO] --- maven-surefire-plugin:2.11:test (default-test) @ server ---
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-api/2.0.9/maven-plugin-api-2.0.9.pom
Progress (1): 1.5 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-api/2.0.9/maven-plugin-api-2.0.9.pom (1.5 kB at 68 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven/2.0.9/maven-2.0.9.pom
Progress (1): 2.2/19 kB
Progress (1): 5.0/19 kB
Progress (1): 7.8/19 kB
Progress (1): 11/19 kB 
Progress (1): 13/19 kB
Progress (1): 16/19 kB
Progress (1): 19/19 kB
Progress (1): 19 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven/2.0.9/maven-2.0.9.pom (19 kB at 540 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/8/maven-parent-8.pom
Progress (1): 2.2/24 kB
Progress (1): 5.0/24 kB
Progress (1): 7.7/24 kB
Progress (1): 11/24 kB 
Progress (1): 13/24 kB
Progress (1): 16/24 kB
Progress (1): 19/24 kB
Progress (1): 21/24 kB
Progress (1): 24 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/8/maven-parent-8.pom (24 kB at 619 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/apache/4/apache-4.pom
Progress (1): 2.8/4.5 kB
Progress (1): 4.5 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/apache/4/apache-4.pom (4.5 kB at 173 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-booter/2.11/surefire-booter-2.11.pom
Progress (1): 2.2/3.0 kB
Progress (1): 3.0 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-booter/2.11/surefire-booter-2.11.pom (3.0 kB at 82 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-api/2.11/surefire-api-2.11.pom
Progress (1): 2.2/2.3 kB
Progress (1): 2.3 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-api/2.11/surefire-api-2.11.pom (2.3 kB at 59 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/maven-surefire-common/2.11/maven-surefire-common-2.11.pom
Progress (1): 2.2/4.0 kB
Progress (1): 4.0 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/maven-surefire-common/2.11/maven-surefire-common-2.11.pom (4.0 kB at 167 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/2.1/plexus-utils-2.1.pom
Progress (1): 2.2/4.0 kB
Progress (1): 4.0 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/2.1/plexus-utils-2.1.pom (4.0 kB at 161 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact/2.0.9/maven-artifact-2.0.9.pom
Progress (1): 1.6 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact/2.0.9/maven-artifact-2.0.9.pom (1.6 kB at 54 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-project/2.0.9/maven-project-2.0.9.pom
Progress (1): 2.2/2.7 kB
Progress (1): 2.7 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-project/2.0.9/maven-project-2.0.9.pom (2.7 kB at 97 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-settings/2.0.9/maven-settings-2.0.9.pom
Progress (1): 2.1 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-settings/2.0.9/maven-settings-2.0.9.pom (2.1 kB at 51 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-model/2.0.9/maven-model-2.0.9.pom
Progress (1): 2.2/3.1 kB
Progress (1): 3.1 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-model/2.0.9/maven-model-2.0.9.pom (3.1 kB at 112 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-profile/2.0.9/maven-profile-2.0.9.pom
Progress (1): 2.0 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-profile/2.0.9/maven-profile-2.0.9.pom (2.0 kB at 82 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact-manager/2.0.9/maven-artifact-manager-2.0.9.pom
Progress (1): 2.2/2.7 kB
Progress (1): 2.7 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact-manager/2.0.9/maven-artifact-manager-2.0.9.pom (2.7 kB at 59 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-repository-metadata/2.0.9/maven-repository-metadata-2.0.9.pom
Progress (1): 1.9 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-repository-metadata/2.0.9/maven-repository-metadata-2.0.9.pom (1.9 kB at 59 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-registry/2.0.9/maven-plugin-registry-2.0.9.pom
Progress (1): 2.0 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-registry/2.0.9/maven-plugin-registry-2.0.9.pom (2.0 kB at 82 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-core/2.0.9/maven-core-2.0.9.pom
Progress (1): 2.8/7.8 kB
Progress (1): 5.5/7.8 kB
Progress (1): 7.8 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-core/2.0.9/maven-core-2.0.9.pom (7.8 kB at 278 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-parameter-documenter/2.0.9/maven-plugin-parameter-documenter-2.0.9.pom
Progress (1): 2.0 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-parameter-documenter/2.0.9/maven-plugin-parameter-documenter-2.0.9.pom (2.0 kB at 82 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/reporting/maven-reporting-api/2.0.9/maven-reporting-api-2.0.9.pom
Progress (1): 1.8 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/reporting/maven-reporting-api/2.0.9/maven-reporting-api-2.0.9.pom (1.8 kB at 72 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/reporting/maven-reporting/2.0.9/maven-reporting-2.0.9.pom
Progress (1): 1.5 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/reporting/maven-reporting/2.0.9/maven-reporting-2.0.9.pom (1.5 kB at 59 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-error-diagnostics/2.0.9/maven-error-diagnostics-2.0.9.pom
Progress (1): 1.7 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-error-diagnostics/2.0.9/maven-error-diagnostics-2.0.9.pom (1.7 kB at 62 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-descriptor/2.0.9/maven-plugin-descriptor-2.0.9.pom
Progress (1): 2.1 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-descriptor/2.0.9/maven-plugin-descriptor-2.0.9.pom (2.1 kB at 69 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-monitor/2.0.9/maven-monitor-2.0.9.pom
Progress (1): 1.3 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-monitor/2.0.9/maven-monitor-2.0.9.pom (1.3 kB at 51 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-toolchain/2.0.9/maven-toolchain-2.0.9.pom
Progress (1): 2.2/3.5 kB
Progress (1): 3.5 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-toolchain/2.0.9/maven-toolchain-2.0.9.pom (3.5 kB at 139 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-common-artifact-filters/1.3/maven-common-artifact-filters-1.3.pom
Progress (1): 2.8/3.7 kB
Progress (1): 3.7 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-common-artifact-filters/1.3/maven-common-artifact-filters-1.3.pom (3.7 kB at 161 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-shared-components/12/maven-shared-components-12.pom
Progress (1): 2.2/9.3 kB
Progress (1): 5.0/9.3 kB
Progress (1): 7.8/9.3 kB
Progress (1): 9.3 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-shared-components/12/maven-shared-components-12.pom (9.3 kB at 346 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/13/maven-parent-13.pom
Progress (1): 2.2/23 kB
Progress (1): 5.0/23 kB
Progress (1): 7.8/23 kB
Progress (1): 11/23 kB 
Progress (1): 13/23 kB
Progress (1): 16/23 kB
Progress (1): 19/23 kB
Progress (1): 21/23 kB
Progress (1): 23 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/13/maven-parent-13.pom (23 kB at 984 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/apache/6/apache-6.pom
Progress (1): 2.2/13 kB
Progress (1): 5.0/13 kB
Progress (1): 7.8/13 kB
Progress (1): 11/13 kB 
Progress (1): 13 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/apache/6/apache-6.pom (13 kB at 376 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-container-default/1.0-alpha-9/plexus-container-default-1.0-alpha-9.pom
Progress (1): 1.2 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-container-default/1.0-alpha-9/plexus-container-default-1.0-alpha-9.pom (1.2 kB at 49 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-api/2.0.9/maven-plugin-api-2.0.9.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-api/2.11/surefire-api-2.11.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-booter/2.11/surefire-booter-2.11.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/maven-surefire-common/2.11/maven-surefire-common-2.11.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-common-artifact-filters/1.3/maven-common-artifact-filters-1.3.jar
Progress (1): 2.8/13 kB
Progress (1): 5.5/13 kB
Progress (1): 8.3/13 kB
Progress (1): 11/13 kB 
Progress (1): 13 kB   
Progress (2): 13 kB | 2.2/160 kB
Progress (2): 13 kB | 5.0/160 kB
Progress (2): 13 kB | 7.7/160 kB
Progress (2): 13 kB | 10/160 kB 
Progress (2): 13 kB | 13/160 kB
Progress (2): 13 kB | 16/160 kB
Progress (2): 13 kB | 19/160 kB
Progress (3): 13 kB | 19/160 kB | 2.2/33 kB
Progress (3): 13 kB | 19/160 kB | 5.0/33 kB
Progress (3): 13 kB | 19/160 kB | 7.7/33 kB
Progress (3): 13 kB | 19/160 kB | 10/33 kB 
Progress (3): 13 kB | 19/160 kB | 13/33 kB
Progress (3): 13 kB | 21/160 kB | 13/33 kB
Progress (3): 13 kB | 21/160 kB | 16/33 kB
Progress (3): 13 kB | 21/160 kB | 19/33 kB
Progress (3): 13 kB | 21/160 kB | 21/33 kB
Progress (3): 13 kB | 21/160 kB | 24/33 kB
Progress (3): 13 kB | 21/160 kB | 27/33 kB
Progress (3): 13 kB | 21/160 kB | 30/33 kB
Progress (3): 13 kB | 24/160 kB | 30/33 kB
Progress (3): 13 kB | 27/160 kB | 30/33 kB
Progress (3): 13 kB | 30/160 kB | 30/33 kB
Progress (3): 13 kB | 32/160 kB | 30/33 kB
Progress (4): 13 kB | 32/160 kB | 30/33 kB | 2.8/31 kB
Progress (4): 13 kB | 32/160 kB | 30/33 kB | 5.5/31 kB
Progress (4): 13 kB | 32/160 kB | 30/33 kB | 8.3/31 kB
Progress (4): 13 kB | 32/160 kB | 32/33 kB | 8.3/31 kB
Progress (5): 13 kB | 32/160 kB | 32/33 kB | 8.3/31 kB | 2.2/84 kB
Progress (5): 13 kB | 36/160 kB | 32/33 kB | 8.3/31 kB | 2.2/84 kB
Progress (5): 13 kB | 36/160 kB | 32/33 kB | 11/31 kB | 2.2/84 kB 
Progress (5): 13 kB | 36/160 kB | 32/33 kB | 14/31 kB | 2.2/84 kB
Progress (5): 13 kB | 36/160 kB | 32/33 kB | 16/31 kB | 2.2/84 kB
Progress (5): 13 kB | 36/160 kB | 32/33 kB | 19/31 kB | 2.2/84 kB
Progress (5): 13 kB | 40/160 kB | 32/33 kB | 19/31 kB | 2.2/84 kB
Progress (5): 13 kB | 40/160 kB | 33 kB | 19/31 kB | 2.2/84 kB   
Progress (5): 13 kB | 40/160 kB | 33 kB | 19/31 kB | 5.0/84 kB
Progress (5): 13 kB | 45/160 kB | 33 kB | 19/31 kB | 5.0/84 kB
Progress (5): 13 kB | 45/160 kB | 33 kB | 19/31 kB | 7.7/84 kB
Progress (5): 13 kB | 45/160 kB | 33 kB | 22/31 kB | 7.7/84 kB
Progress (5): 13 kB | 45/160 kB | 33 kB | 22/31 kB | 10/84 kB 
Progress (5): 13 kB | 45/160 kB | 33 kB | 25/31 kB | 10/84 kB
Progress (5): 13 kB | 45/160 kB | 33 kB | 27/31 kB | 10/84 kB
Progress (5): 13 kB | 49/160 kB | 33 kB | 27/31 kB | 10/84 kB
Progress (5): 13 kB | 49/160 kB | 33 kB | 30/31 kB | 10/84 kB
Progress (5): 13 kB | 49/160 kB | 33 kB | 30/31 kB | 13/84 kB
Progress (5): 13 kB | 49/160 kB | 33 kB | 31 kB | 13/84 kB   
Progress (5): 13 kB | 53/160 kB | 33 kB | 31 kB | 13/84 kB
Progress (5): 13 kB | 57/160 kB | 33 kB | 31 kB | 13/84 kB
Progress (5): 13 kB | 61/160 kB | 33 kB | 31 kB | 13/84 kB
Progress (5): 13 kB | 65/160 kB | 33 kB | 31 kB | 13/84 kB
Progress (5): 13 kB | 65/160 kB | 33 kB | 31 kB | 16/84 kB
Progress (5): 13 kB | 69/160 kB | 33 kB | 31 kB | 16/84 kB
Progress (5): 13 kB | 69/160 kB | 33 kB | 31 kB | 19/84 kB
Progress (5): 13 kB | 73/160 kB | 33 kB | 31 kB | 19/84 kB
Progress (5): 13 kB | 73/160 kB | 33 kB | 31 kB | 21/84 kB
Progress (5): 13 kB | 77/160 kB | 33 kB | 31 kB | 21/84 kB
Progress (5): 13 kB | 77/160 kB | 33 kB | 31 kB | 24/84 kB
Progress (5): 13 kB | 81/160 kB | 33 kB | 31 kB | 24/84 kB
Progress (5): 13 kB | 81/160 kB | 33 kB | 31 kB | 27/84 kB
Progress (5): 13 kB | 85/160 kB | 33 kB | 31 kB | 27/84 kB
Progress (5): 13 kB | 85/160 kB | 33 kB | 31 kB | 30/84 kB
Progress (5): 13 kB | 90/160 kB | 33 kB | 31 kB | 30/84 kB
Progress (5): 13 kB | 90/160 kB | 33 kB | 31 kB | 32/84 kB
Progress (5): 13 kB | 94/160 kB | 33 kB | 31 kB | 32/84 kB
Progress (5): 13 kB | 98/160 kB | 33 kB | 31 kB | 32/84 kB
Progress (5): 13 kB | 98/160 kB | 33 kB | 31 kB | 36/84 kB
Progress (5): 13 kB | 102/160 kB | 33 kB | 31 kB | 36/84 kB
Progress (5): 13 kB | 102/160 kB | 33 kB | 31 kB | 40/84 kB
Progress (5): 13 kB | 106/160 kB | 33 kB | 31 kB | 40/84 kB
Progress (5): 13 kB | 110/160 kB | 33 kB | 31 kB | 40/84 kB
                                                           
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-api/2.0.9/maven-plugin-api-2.0.9.jar (13 kB at 293 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/2.1/plexus-utils-2.1.jar
Progress (4): 114/160 kB | 33 kB | 31 kB | 40/84 kB
Progress (4): 114/160 kB | 33 kB | 31 kB | 45/84 kB
Progress (4): 114/160 kB | 33 kB | 31 kB | 49/84 kB
Progress (4): 118/160 kB | 33 kB | 31 kB | 49/84 kB
Progress (4): 122/160 kB | 33 kB | 31 kB | 49/84 kB
Progress (4): 126/160 kB | 33 kB | 31 kB | 49/84 kB
Progress (4): 131/160 kB | 33 kB | 31 kB | 49/84 kB
Progress (4): 131/160 kB | 33 kB | 31 kB | 53/84 kB
Progress (4): 131/160 kB | 33 kB | 31 kB | 57/84 kB
Progress (4): 135/160 kB | 33 kB | 31 kB | 57/84 kB
Progress (4): 135/160 kB | 33 kB | 31 kB | 61/84 kB
Progress (4): 139/160 kB | 33 kB | 31 kB | 61/84 kB
Progress (4): 139/160 kB | 33 kB | 31 kB | 65/84 kB
Progress (4): 143/160 kB | 33 kB | 31 kB | 65/84 kB
                                                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-common-artifact-filters/1.3/maven-common-artifact-filters-1.3.jar (31 kB at 675 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact/2.0.9/maven-artifact-2.0.9.jar
Progress (3): 147/160 kB | 33 kB | 65/84 kB
Progress (3): 147/160 kB | 33 kB | 69/84 kB
Progress (3): 147/160 kB | 33 kB | 73/84 kB
Progress (3): 147/160 kB | 33 kB | 77/84 kB
Progress (3): 147/160 kB | 33 kB | 81/84 kB
                                           
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-booter/2.11/surefire-booter-2.11.jar (33 kB at 656 kB/s)
Progress (2): 147/160 kB | 84 kB
                                
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-project/2.0.9/maven-project-2.0.9.jar
Progress (2): 151/160 kB | 84 kB
Progress (2): 155/160 kB | 84 kB
Progress (2): 159/160 kB | 84 kB
Progress (2): 160 kB | 84 kB    
Progress (3): 160 kB | 84 kB | 2.2/225 kB
Progress (3): 160 kB | 84 kB | 5.0/225 kB
Progress (3): 160 kB | 84 kB | 7.7/225 kB
Progress (3): 160 kB | 84 kB | 10/225 kB 
Progress (3): 160 kB | 84 kB | 13/225 kB
Progress (3): 160 kB | 84 kB | 16/225 kB
Progress (3): 160 kB | 84 kB | 19/225 kB
Progress (3): 160 kB | 84 kB | 21/225 kB
Progress (3): 160 kB | 84 kB | 24/225 kB
Progress (3): 160 kB | 84 kB | 27/225 kB
Progress (3): 160 kB | 84 kB | 30/225 kB
Progress (3): 160 kB | 84 kB | 32/225 kB
Progress (3): 160 kB | 84 kB | 36/225 kB
Progress (3): 160 kB | 84 kB | 40/225 kB
Progress (3): 160 kB | 84 kB | 45/225 kB
Progress (3): 160 kB | 84 kB | 49/225 kB
Progress (3): 160 kB | 84 kB | 53/225 kB
Progress (3): 160 kB | 84 kB | 57/225 kB
Progress (3): 160 kB | 84 kB | 61/225 kB
Progress (3): 160 kB | 84 kB | 65/225 kB
Progress (3): 160 kB | 84 kB | 69/225 kB
Progress (3): 160 kB | 84 kB | 73/225 kB
Progress (3): 160 kB | 84 kB | 77/225 kB
Progress (3): 160 kB | 84 kB | 81/225 kB
Progress (3): 160 kB | 84 kB | 85/225 kB
Progress (3): 160 kB | 84 kB | 90/225 kB
Progress (3): 160 kB | 84 kB | 94/225 kB
Progress (3): 160 kB | 84 kB | 98/225 kB
Progress (3): 160 kB | 84 kB | 102/225 kB
Progress (3): 160 kB | 84 kB | 106/225 kB
Progress (3): 160 kB | 84 kB | 110/225 kB
Progress (3): 160 kB | 84 kB | 114/225 kB
Progress (3): 160 kB | 84 kB | 118/225 kB
Progress (4): 160 kB | 84 kB | 118/225 kB | 2.2/89 kB
                                                     
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-api/2.11/surefire-api-2.11.jar (160 kB at 1.8 MB/s)
Progress (3): 84 kB | 122/225 kB | 2.2/89 kB
Progress (3): 84 kB | 122/225 kB | 5.0/89 kB
Progress (3): 84 kB | 122/225 kB | 7.7/89 kB
Progress (3): 84 kB | 122/225 kB | 10/89 kB 
Progress (4): 84 kB | 122/225 kB | 10/89 kB | 2.2/122 kB
Progress (4): 84 kB | 122/225 kB | 10/89 kB | 5.0/122 kB
Progress (4): 84 kB | 122/225 kB | 10/89 kB | 7.7/122 kB
                                                        
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/maven-surefire-common/2.11/maven-surefire-common-2.11.jar (84 kB at 934 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-settings/2.0.9/maven-settings-2.0.9.jar
Progress (3): 122/225 kB | 10/89 kB | 10/122 kB
Progress (3): 122/225 kB | 10/89 kB | 13/122 kB
Progress (3): 122/225 kB | 10/89 kB | 16/122 kB
Progress (3): 122/225 kB | 10/89 kB | 19/122 kB
Progress (3): 122/225 kB | 10/89 kB | 21/122 kB
Progress (3): 122/225 kB | 10/89 kB | 24/122 kB
Progress (3): 122/225 kB | 10/89 kB | 27/122 kB
Progress (3): 122/225 kB | 10/89 kB | 30/122 kB
Progress (3): 122/225 kB | 10/89 kB | 32/122 kB
Progress (3): 122/225 kB | 10/89 kB | 36/122 kB
Progress (3): 122/225 kB | 10/89 kB | 40/122 kB
Progress (3): 122/225 kB | 10/89 kB | 45/122 kB
Progress (3): 122/225 kB | 10/89 kB | 49/122 kB
Progress (3): 122/225 kB | 10/89 kB | 53/122 kB
Progress (3): 122/225 kB | 10/89 kB | 57/122 kB
Progress (3): 122/225 kB | 10/89 kB | 61/122 kB
Progress (3): 122/225 kB | 10/89 kB | 65/122 kB
Progress (3): 122/225 kB | 10/89 kB | 69/122 kB
Progress (3): 122/225 kB | 10/89 kB | 73/122 kB
Progress (3): 122/225 kB | 10/89 kB | 77/122 kB
Progress (3): 122/225 kB | 10/89 kB | 81/122 kB
                                               
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-profile/2.0.9/maven-profile-2.0.9.jar
Progress (3): 126/225 kB | 10/89 kB | 81/122 kB
Progress (3): 126/225 kB | 13/89 kB | 81/122 kB
Progress (3): 126/225 kB | 16/89 kB | 81/122 kB
Progress (3): 126/225 kB | 19/89 kB | 81/122 kB
Progress (3): 126/225 kB | 21/89 kB | 81/122 kB
Progress (3): 126/225 kB | 24/89 kB | 81/122 kB
Progress (3): 126/225 kB | 27/89 kB | 81/122 kB
Progress (3): 126/225 kB | 30/89 kB | 81/122 kB
Progress (3): 126/225 kB | 32/89 kB | 81/122 kB
Progress (3): 126/225 kB | 36/89 kB | 81/122 kB
Progress (3): 126/225 kB | 40/89 kB | 81/122 kB
Progress (3): 126/225 kB | 45/89 kB | 81/122 kB
Progress (3): 126/225 kB | 49/89 kB | 81/122 kB
Progress (3): 126/225 kB | 53/89 kB | 81/122 kB
Progress (3): 126/225 kB | 57/89 kB | 81/122 kB
Progress (3): 126/225 kB | 61/89 kB | 81/122 kB
Progress (3): 126/225 kB | 65/89 kB | 81/122 kB
Progress (4): 126/225 kB | 65/89 kB | 81/122 kB | 4.1/49 kB
Progress (4): 126/225 kB | 69/89 kB | 81/122 kB | 4.1/49 kB
Progress (4): 126/225 kB | 69/89 kB | 81/122 kB | 7.7/49 kB
Progress (4): 126/225 kB | 73/89 kB | 81/122 kB | 7.7/49 kB
Progress (4): 126/225 kB | 73/89 kB | 85/122 kB | 7.7/49 kB
Progress (4): 126/225 kB | 77/89 kB | 85/122 kB | 7.7/49 kB
Progress (4): 131/225 kB | 77/89 kB | 85/122 kB | 7.7/49 kB
Progress (4): 131/225 kB | 77/89 kB | 85/122 kB | 12/49 kB 
Progress (4): 131/225 kB | 77/89 kB | 90/122 kB | 12/49 kB
Progress (4): 131/225 kB | 81/89 kB | 90/122 kB | 12/49 kB
Progress (4): 131/225 kB | 85/89 kB | 90/122 kB | 12/49 kB
Progress (4): 131/225 kB | 85/89 kB | 90/122 kB | 16/49 kB
Progress (4): 135/225 kB | 85/89 kB | 90/122 kB | 16/49 kB
Progress (4): 139/225 kB | 85/89 kB | 90/122 kB | 16/49 kB
Progress (4): 143/225 kB | 85/89 kB | 90/122 kB | 16/49 kB
Progress (4): 147/225 kB | 85/89 kB | 90/122 kB | 16/49 kB
Progress (4): 151/225 kB | 85/89 kB | 90/122 kB | 16/49 kB
Progress (4): 155/225 kB | 85/89 kB | 90/122 kB | 16/49 kB
Progress (4): 159/225 kB | 85/89 kB | 90/122 kB | 16/49 kB
Progress (4): 163/225 kB | 85/89 kB | 90/122 kB | 16/49 kB
Progress (4): 167/225 kB | 85/89 kB | 90/122 kB | 16/49 kB
Progress (4): 171/225 kB | 85/89 kB | 90/122 kB | 16/49 kB
Progress (4): 176/225 kB | 85/89 kB | 90/122 kB | 16/49 kB
Progress (4): 180/225 kB | 85/89 kB | 90/122 kB | 16/49 kB
Progress (4): 180/225 kB | 89 kB | 90/122 kB | 16/49 kB   
Progress (4): 180/225 kB | 89 kB | 94/122 kB | 16/49 kB
Progress (4): 180/225 kB | 89 kB | 98/122 kB | 16/49 kB
Progress (4): 180/225 kB | 89 kB | 102/122 kB | 16/49 kB
Progress (4): 180/225 kB | 89 kB | 106/122 kB | 16/49 kB
Progress (4): 180/225 kB | 89 kB | 110/122 kB | 16/49 kB
Progress (4): 180/225 kB | 89 kB | 114/122 kB | 16/49 kB
Progress (4): 180/225 kB | 89 kB | 118/122 kB | 16/49 kB
Progress (4): 180/225 kB | 89 kB | 122 kB | 16/49 kB    
Progress (5): 180/225 kB | 89 kB | 122 kB | 16/49 kB | 4.1/35 kB
Progress (5): 180/225 kB | 89 kB | 122 kB | 20/49 kB | 4.1/35 kB
Progress (5): 180/225 kB | 89 kB | 122 kB | 20/49 kB | 7.7/35 kB
Progress (5): 184/225 kB | 89 kB | 122 kB | 20/49 kB | 7.7/35 kB
Progress (5): 184/225 kB | 89 kB | 122 kB | 20/49 kB | 12/35 kB 
Progress (5): 184/225 kB | 89 kB | 122 kB | 24/49 kB | 12/35 kB
Progress (5): 184/225 kB | 89 kB | 122 kB | 24/49 kB | 16/35 kB
Progress (5): 188/225 kB | 89 kB | 122 kB | 24/49 kB | 16/35 kB
Progress (5): 188/225 kB | 89 kB | 122 kB | 28/49 kB | 16/35 kB
Progress (5): 188/225 kB | 89 kB | 122 kB | 28/49 kB | 20/35 kB
Progress (5): 192/225 kB | 89 kB | 122 kB | 28/49 kB | 20/35 kB
Progress (5): 192/225 kB | 89 kB | 122 kB | 28/49 kB | 24/35 kB
Progress (5): 192/225 kB | 89 kB | 122 kB | 32/49 kB | 24/35 kB
Progress (5): 196/225 kB | 89 kB | 122 kB | 32/49 kB | 24/35 kB
Progress (5): 196/225 kB | 89 kB | 122 kB | 32/49 kB | 28/35 kB
Progress (5): 196/225 kB | 89 kB | 122 kB | 32/49 kB | 32/35 kB
Progress (5): 196/225 kB | 89 kB | 122 kB | 32/49 kB | 35 kB   
Progress (5): 196/225 kB | 89 kB | 122 kB | 36/49 kB | 35 kB
Progress (5): 200/225 kB | 89 kB | 122 kB | 36/49 kB | 35 kB
Progress (5): 200/225 kB | 89 kB | 122 kB | 40/49 kB | 35 kB
Progress (5): 204/225 kB | 89 kB | 122 kB | 40/49 kB | 35 kB
                                                            
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact/2.0.9/maven-artifact-2.0.9.jar (89 kB at 674 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-model/2.0.9/maven-model-2.0.9.jar
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-project/2.0.9/maven-project-2.0.9.jar (122 kB at 923 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact-manager/2.0.9/maven-artifact-manager-2.0.9.jar
Progress (3): 204/225 kB | 45/49 kB | 35 kB
Progress (3): 208/225 kB | 45/49 kB | 35 kB
Progress (3): 208/225 kB | 49/49 kB | 35 kB
Progress (3): 212/225 kB | 49/49 kB | 35 kB
Progress (3): 212/225 kB | 49 kB | 35 kB   
Progress (3): 217/225 kB | 49 kB | 35 kB
Progress (3): 221/225 kB | 49 kB | 35 kB
Progress (3): 225 kB | 49 kB | 35 kB    
                                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-profile/2.0.9/maven-profile-2.0.9.jar (35 kB at 260 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-registry/2.0.9/maven-plugin-registry-2.0.9.jar
Progress (3): 225 kB | 49 kB | 2.8/87 kB
Progress (3): 225 kB | 49 kB | 5.5/87 kB
Progress (4): 225 kB | 49 kB | 5.5/87 kB | 4.1/58 kB
Progress (4): 225 kB | 49 kB | 5.5/87 kB | 7.6/58 kB
Progress (4): 225 kB | 49 kB | 5.5/87 kB | 12/58 kB 
Progress (4): 225 kB | 49 kB | 5.5/87 kB | 16/58 kB
Progress (4): 225 kB | 49 kB | 8.3/87 kB | 16/58 kB
Progress (4): 225 kB | 49 kB | 11/87 kB | 16/58 kB 
Progress (4): 225 kB | 49 kB | 11/87 kB | 20/58 kB
Progress (4): 225 kB | 49 kB | 11/87 kB | 24/58 kB
Progress (4): 225 kB | 49 kB | 11/87 kB | 28/58 kB
Progress (4): 225 kB | 49 kB | 11/87 kB | 32/58 kB
Progress (4): 225 kB | 49 kB | 11/87 kB | 36/58 kB
Progress (4): 225 kB | 49 kB | 11/87 kB | 40/58 kB
Progress (4): 225 kB | 49 kB | 11/87 kB | 45/58 kB
Progress (4): 225 kB | 49 kB | 11/87 kB | 49/58 kB
Progress (4): 225 kB | 49 kB | 11/87 kB | 53/58 kB
Progress (4): 225 kB | 49 kB | 11/87 kB | 57/58 kB
Progress (4): 225 kB | 49 kB | 11/87 kB | 58 kB   
                                               
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/2.1/plexus-utils-2.1.jar (225 kB at 1.4 MB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-core/2.0.9/maven-core-2.0.9.jar
Progress (3): 49 kB | 14/87 kB | 58 kB
                                      
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-settings/2.0.9/maven-settings-2.0.9.jar (49 kB at 307 kB/s)
Progress (3): 14/87 kB | 58 kB | 4.1/29 kB
                                          
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-parameter-documenter/2.0.9/maven-plugin-parameter-documenter-2.0.9.jar
Progress (3): 16/87 kB | 58 kB | 4.1/29 kB
Progress (3): 16/87 kB | 58 kB | 7.6/29 kB
Progress (3): 19/87 kB | 58 kB | 7.6/29 kB
Progress (3): 19/87 kB | 58 kB | 12/29 kB 
Progress (3): 22/87 kB | 58 kB | 12/29 kB
Progress (3): 22/87 kB | 58 kB | 16/29 kB
Progress (3): 25/87 kB | 58 kB | 16/29 kB
Progress (3): 25/87 kB | 58 kB | 20/29 kB
Progress (3): 25/87 kB | 58 kB | 24/29 kB
Progress (3): 27/87 kB | 58 kB | 24/29 kB
Progress (3): 27/87 kB | 58 kB | 28/29 kB
Progress (3): 27/87 kB | 58 kB | 29 kB   
Progress (3): 30/87 kB | 58 kB | 29 kB
Progress (3): 33/87 kB | 58 kB | 29 kB
Progress (3): 37/87 kB | 58 kB | 29 kB
Progress (3): 41/87 kB | 58 kB | 29 kB
Progress (3): 45/87 kB | 58 kB | 29 kB
Progress (3): 49/87 kB | 58 kB | 29 kB
                                      
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-artifact-manager/2.0.9/maven-artifact-manager-2.0.9.jar (58 kB at 342 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/reporting/maven-reporting-api/2.0.9/maven-reporting-api-2.0.9.jar
Progress (2): 53/87 kB | 29 kB
Progress (3): 53/87 kB | 29 kB | 4.1/160 kB
Progress (3): 53/87 kB | 29 kB | 8.2/160 kB
Progress (3): 53/87 kB | 29 kB | 12/160 kB 
Progress (4): 53/87 kB | 29 kB | 12/160 kB | 2.2/21 kB
Progress (4): 57/87 kB | 29 kB | 12/160 kB | 2.2/21 kB
Progress (4): 61/87 kB | 29 kB | 12/160 kB | 2.2/21 kB
Progress (4): 66/87 kB | 29 kB | 12/160 kB | 2.2/21 kB
Progress (4): 66/87 kB | 29 kB | 16/160 kB | 2.2/21 kB
                                                      
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-registry/2.0.9/maven-plugin-registry-2.0.9.jar (29 kB at 162 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-repository-metadata/2.0.9/maven-repository-metadata-2.0.9.jar
Progress (3): 66/87 kB | 16/160 kB | 4.1/21 kB
Progress (3): 66/87 kB | 20/160 kB | 4.1/21 kB
Progress (3): 66/87 kB | 20/160 kB | 6.8/21 kB
Progress (3): 70/87 kB | 20/160 kB | 6.8/21 kB
Progress (3): 74/87 kB | 20/160 kB | 6.8/21 kB
Progress (3): 78/87 kB | 20/160 kB | 6.8/21 kB
Progress (3): 78/87 kB | 25/160 kB | 6.8/21 kB
Progress (3): 78/87 kB | 25/160 kB | 9.6/21 kB
Progress (3): 82/87 kB | 25/160 kB | 9.6/21 kB
Progress (3): 82/87 kB | 25/160 kB | 12/21 kB 
Progress (3): 82/87 kB | 29/160 kB | 12/21 kB
Progress (3): 82/87 kB | 29/160 kB | 15/21 kB
Progress (3): 86/87 kB | 29/160 kB | 15/21 kB
Progress (3): 86/87 kB | 33/160 kB | 15/21 kB
Progress (3): 86/87 kB | 33/160 kB | 18/21 kB
Progress (3): 86/87 kB | 33/160 kB | 20/21 kB
Progress (3): 86/87 kB | 33/160 kB | 21 kB   
Progress (3): 86/87 kB | 37/160 kB | 21 kB
Progress (3): 86/87 kB | 41/160 kB | 21 kB
Progress (3): 86/87 kB | 45/160 kB | 21 kB
Progress (3): 86/87 kB | 49/160 kB | 21 kB
Progress (4): 86/87 kB | 49/160 kB | 21 kB | 4.1/10 kB
Progress (4): 86/87 kB | 49/160 kB | 21 kB | 7.7/10 kB
Progress (4): 86/87 kB | 49/160 kB | 21 kB | 10 kB    
Progress (4): 87 kB | 49/160 kB | 21 kB | 10 kB   
Progress (4): 87 kB | 53/160 kB | 21 kB | 10 kB
Progress (4): 87 kB | 57/160 kB | 21 kB | 10 kB
Progress (4): 87 kB | 61/160 kB | 21 kB | 10 kB
Progress (4): 87 kB | 66/160 kB | 21 kB | 10 kB
                                               
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-parameter-documenter/2.0.9/maven-plugin-parameter-documenter-2.0.9.jar (21 kB at 105 kB/s)
Progress (3): 87 kB | 70/160 kB | 10 kB
Progress (4): 87 kB | 70/160 kB | 10 kB | 4.1/25 kB
Progress (4): 87 kB | 74/160 kB | 10 kB | 4.1/25 kB
                                                   
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-error-diagnostics/2.0.9/maven-error-diagnostics-2.0.9.jar
Progress (4): 87 kB | 78/160 kB | 10 kB | 4.1/25 kB
Progress (4): 87 kB | 78/160 kB | 10 kB | 7.7/25 kB
Progress (4): 87 kB | 82/160 kB | 10 kB | 7.7/25 kB
Progress (4): 87 kB | 82/160 kB | 10 kB | 12/25 kB 
Progress (4): 87 kB | 82/160 kB | 10 kB | 16/25 kB
Progress (4): 87 kB | 82/160 kB | 10 kB | 20/25 kB
Progress (4): 87 kB | 82/160 kB | 10 kB | 24/25 kB
Progress (4): 87 kB | 82/160 kB | 10 kB | 25 kB   
                                               
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-model/2.0.9/maven-model-2.0.9.jar (87 kB at 420 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-descriptor/2.0.9/maven-plugin-descriptor-2.0.9.jar
Progress (3): 86/160 kB | 10 kB | 25 kB
Progress (3): 90/160 kB | 10 kB | 25 kB
Progress (3): 94/160 kB | 10 kB | 25 kB
Progress (3): 98/160 kB | 10 kB | 25 kB
Progress (3): 102/160 kB | 10 kB | 25 kB
Progress (3): 106/160 kB | 10 kB | 25 kB
Progress (3): 111/160 kB | 10 kB | 25 kB
Progress (3): 115/160 kB | 10 kB | 25 kB
Progress (3): 119/160 kB | 10 kB | 25 kB
Progress (3): 123/160 kB | 10 kB | 25 kB
Progress (3): 127/160 kB | 10 kB | 25 kB
Progress (3): 131/160 kB | 10 kB | 25 kB
Progress (3): 135/160 kB | 10 kB | 25 kB
Progress (3): 139/160 kB | 10 kB | 25 kB
Progress (3): 143/160 kB | 10 kB | 25 kB
Progress (3): 147/160 kB | 10 kB | 25 kB
Progress (4): 147/160 kB | 10 kB | 25 kB | 2.8/14 kB
Progress (4): 147/160 kB | 10 kB | 25 kB | 5.5/14 kB
Progress (4): 152/160 kB | 10 kB | 25 kB | 5.5/14 kB
Progress (4): 152/160 kB | 10 kB | 25 kB | 8.3/14 kB
Progress (4): 156/160 kB | 10 kB | 25 kB | 8.3/14 kB
Progress (4): 156/160 kB | 10 kB | 25 kB | 11/14 kB 
Progress (4): 160 kB | 10 kB | 25 kB | 11/14 kB    
Progress (4): 160 kB | 10 kB | 25 kB | 14 kB   
                                            
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/reporting/maven-reporting-api/2.0.9/maven-reporting-api-2.0.9.jar (10 kB at 46 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-monitor/2.0.9/maven-monitor-2.0.9.jar
Progress (4): 160 kB | 25 kB | 14 kB | 4.1/37 kB
                                                
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-repository-metadata/2.0.9/maven-repository-metadata-2.0.9.jar (25 kB at 111 kB/s)
Progress (3): 160 kB | 14 kB | 7.7/37 kB
                                        
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-toolchain/2.0.9/maven-toolchain-2.0.9.jar
Progress (3): 160 kB | 14 kB | 12/37 kB
Progress (3): 160 kB | 14 kB | 16/37 kB
Progress (3): 160 kB | 14 kB | 20/37 kB
Progress (3): 160 kB | 14 kB | 24/37 kB
Progress (3): 160 kB | 14 kB | 28/37 kB
Progress (3): 160 kB | 14 kB | 32/37 kB
Progress (3): 160 kB | 14 kB | 36/37 kB
Progress (3): 160 kB | 14 kB | 37 kB   
                                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-error-diagnostics/2.0.9/maven-error-diagnostics-2.0.9.jar (14 kB at 60 kB/s)
Progress (3): 160 kB | 37 kB | 4.1/10 kB
Progress (3): 160 kB | 37 kB | 8.2/10 kB
Progress (3): 160 kB | 37 kB | 10 kB    
Progress (4): 160 kB | 37 kB | 10 kB | 2.8/38 kB
Progress (4): 160 kB | 37 kB | 10 kB | 5.5/38 kB
Progress (4): 160 kB | 37 kB | 10 kB | 8.3/38 kB
Progress (4): 160 kB | 37 kB | 10 kB | 11/38 kB 
Progress (4): 160 kB | 37 kB | 10 kB | 14/38 kB
Progress (4): 160 kB | 37 kB | 10 kB | 16/38 kB
Progress (4): 160 kB | 37 kB | 10 kB | 19/38 kB
Progress (4): 160 kB | 37 kB | 10 kB | 22/38 kB
Progress (4): 160 kB | 37 kB | 10 kB | 25/38 kB
Progress (4): 160 kB | 37 kB | 10 kB | 27/38 kB
Progress (4): 160 kB | 37 kB | 10 kB | 30/38 kB
Progress (4): 160 kB | 37 kB | 10 kB | 33/38 kB
Progress (4): 160 kB | 37 kB | 10 kB | 36/38 kB
Progress (4): 160 kB | 37 kB | 10 kB | 38 kB   
                                            
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-plugin-descriptor/2.0.9/maven-plugin-descriptor-2.0.9.jar (37 kB at 153 kB/s)
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-core/2.0.9/maven-core-2.0.9.jar (160 kB at 654 kB/s)
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-monitor/2.0.9/maven-monitor-2.0.9.jar (10 kB at 42 kB/s)
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-toolchain/2.0.9/maven-toolchain-2.0.9.jar (38 kB at 148 kB/s)
[INFO] Surefire report directory: /home/jenkins/workspace/First_Pipeline/server/target/surefire-reports
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-junit4/2.11/surefire-junit4-2.11.pom
Progress (1): 2.2/2.6 kB
Progress (1): 2.6 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-junit4/2.11/surefire-junit4-2.11.pom (2.6 kB at 90 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-providers/2.11/surefire-providers-2.11.pom
Progress (1): 2.2/2.3 kB
Progress (1): 2.3 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-providers/2.11/surefire-providers-2.11.pom (2.3 kB at 85 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-junit4/2.11/surefire-junit4-2.11.jar
Progress (1): 2.2/33 kB
Progress (1): 5.0/33 kB
Progress (1): 7.7/33 kB
Progress (1): 10/33 kB 
Progress (1): 13/33 kB
Progress (1): 16/33 kB
Progress (1): 19/33 kB
Progress (1): 21/33 kB
Progress (1): 24/33 kB
Progress (1): 27/33 kB
Progress (1): 30/33 kB
Progress (1): 32/33 kB
Progress (1): 33 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-junit4/2.11/surefire-junit4-2.11.jar (33 kB at 1.2 MB/s)

-------------------------------------------------------
 T E S T S
-------------------------------------------------------
Running com.example.TestGreeter
Tests run: 2, Failures: 0, Errors: 0, Skipped: 0, Time elapsed: 0.071 sec

Results :

Tests run: 2, Failures: 0, Errors: 0, Skipped: 0

[INFO] 
[INFO] --- maven-jar-plugin:2.4:jar (default-jar) @ server ---
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-archiver/2.5/maven-archiver-2.5.pom
Progress (1): 2.8/4.5 kB
Progress (1): 4.5 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-archiver/2.5/maven-archiver-2.5.pom (4.5 kB at 197 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-shared-components/17/maven-shared-components-17.pom
Progress (1): 2.8/8.7 kB
Progress (1): 5.5/8.7 kB
Progress (1): 8.3/8.7 kB
Progress (1): 8.7 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-shared-components/17/maven-shared-components-17.pom (8.7 kB at 335 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-archiver/2.1/plexus-archiver-2.1.pom
Progress (1): 2.8/2.8 kB
Progress (1): 2.8 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-archiver/2.1/plexus-archiver-2.1.pom (2.8 kB at 122 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/sonatype/spice/spice-parent/17/spice-parent-17.pom
Progress (1): 0.5/6.8 kB
Progress (1): 3.3/6.8 kB
Progress (1): 6.0/6.8 kB
Progress (1): 6.8 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/sonatype/spice/spice-parent/17/spice-parent-17.pom (6.8 kB at 270 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/sonatype/forge/forge-parent/10/forge-parent-10.pom
Progress (1): 2.2/14 kB
Progress (1): 4.9/14 kB
Progress (1): 7.7/14 kB
Progress (1): 10/14 kB 
Progress (1): 13/14 kB
Progress (1): 14 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/sonatype/forge/forge-parent/10/forge-parent-10.pom (14 kB at 502 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-io/2.0.2/plexus-io-2.0.2.pom
Progress (1): 1.7 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-io/2.0.2/plexus-io-2.0.2.pom (1.7 kB at 69 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-components/1.1.19/plexus-components-1.1.19.pom
Progress (1): 2.2/2.7 kB
Progress (1): 2.7 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-components/1.1.19/plexus-components-1.1.19.pom (2.7 kB at 104 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/3.0.1/plexus-3.0.1.pom
Progress (1): 2.8/19 kB
Progress (1): 5.5/19 kB
Progress (1): 8.3/19 kB
Progress (1): 11/19 kB 
Progress (1): 14/19 kB
Progress (1): 16/19 kB
Progress (1): 19 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/3.0.1/plexus-3.0.1.pom (19 kB at 665 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interpolation/1.15/plexus-interpolation-1.15.pom
Progress (1): 1.0 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interpolation/1.15/plexus-interpolation-1.15.pom (1.0 kB at 41 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/commons-lang/commons-lang/2.1/commons-lang-2.1.pom
Progress (1): 2.2/9.9 kB
Progress (1): 5.0/9.9 kB
Progress (1): 7.7/9.9 kB
Progress (1): 9.9 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/commons-lang/commons-lang/2.1/commons-lang-2.1.pom (9.9 kB at 382 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-archiver/2.5/maven-archiver-2.5.jar
Downloading from central: https://repo.maven.apache.org/maven2/classworlds/classworlds/1.1-alpha-2/classworlds-1.1-alpha-2.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interpolation/1.15/plexus-interpolation-1.15.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-io/2.0.2/plexus-io-2.0.2.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-archiver/2.1/plexus-archiver-2.1.jar
Progress (1): 2.8/22 kB
Progress (1): 5.5/22 kB
Progress (1): 8.3/22 kB
Progress (1): 11/22 kB 
Progress (1): 14/22 kB
Progress (1): 16/22 kB
Progress (1): 19/22 kB
Progress (1): 22 kB   
Progress (2): 22 kB | 2.2/38 kB
Progress (2): 22 kB | 5.0/38 kB
Progress (2): 22 kB | 7.7/38 kB
Progress (2): 22 kB | 10/38 kB 
Progress (2): 22 kB | 13/38 kB
Progress (2): 22 kB | 16/38 kB
Progress (2): 22 kB | 19/38 kB
Progress (3): 22 kB | 19/38 kB | 2.8/58 kB
Progress (3): 22 kB | 21/38 kB | 2.8/58 kB
Progress (3): 22 kB | 21/38 kB | 5.5/58 kB
Progress (3): 22 kB | 24/38 kB | 5.5/58 kB
Progress (3): 22 kB | 24/38 kB | 8.3/58 kB
Progress (4): 22 kB | 24/38 kB | 8.3/58 kB | 2.8/60 kB
Progress (4): 22 kB | 27/38 kB | 8.3/58 kB | 2.8/60 kB
Progress (4): 22 kB | 27/38 kB | 8.3/58 kB | 5.5/60 kB
Progress (4): 22 kB | 27/38 kB | 8.3/58 kB | 8.3/60 kB
Progress (4): 22 kB | 27/38 kB | 11/58 kB | 8.3/60 kB 
Progress (4): 22 kB | 27/38 kB | 11/58 kB | 11/60 kB 
Progress (4): 22 kB | 27/38 kB | 14/58 kB | 11/60 kB
Progress (4): 22 kB | 30/38 kB | 14/58 kB | 11/60 kB
Progress (4): 22 kB | 30/38 kB | 16/58 kB | 11/60 kB
Progress (4): 22 kB | 30/38 kB | 16/58 kB | 14/60 kB
Progress (4): 22 kB | 30/38 kB | 19/58 kB | 14/60 kB
Progress (4): 22 kB | 32/38 kB | 19/58 kB | 14/60 kB
Progress (4): 22 kB | 32/38 kB | 22/58 kB | 14/60 kB
Progress (4): 22 kB | 32/38 kB | 22/58 kB | 16/60 kB
Progress (4): 22 kB | 32/38 kB | 25/58 kB | 16/60 kB
Progress (4): 22 kB | 35/38 kB | 25/58 kB | 16/60 kB
Progress (4): 22 kB | 35/38 kB | 27/58 kB | 16/60 kB
Progress (4): 22 kB | 35/38 kB | 27/58 kB | 19/60 kB
Progress (4): 22 kB | 35/38 kB | 30/58 kB | 19/60 kB
Progress (4): 22 kB | 38 kB | 30/58 kB | 19/60 kB   
Progress (4): 22 kB | 38 kB | 33/58 kB | 19/60 kB
Progress (4): 22 kB | 38 kB | 33/58 kB | 22/60 kB
Progress (4): 22 kB | 38 kB | 33/58 kB | 25/60 kB
Progress (4): 22 kB | 38 kB | 33/58 kB | 27/60 kB
Progress (4): 22 kB | 38 kB | 33/58 kB | 30/60 kB
Progress (4): 22 kB | 38 kB | 37/58 kB | 30/60 kB
Progress (4): 22 kB | 38 kB | 37/58 kB | 33/60 kB
Progress (4): 22 kB | 38 kB | 41/58 kB | 33/60 kB
Progress (4): 22 kB | 38 kB | 45/58 kB | 33/60 kB
Progress (4): 22 kB | 38 kB | 49/58 kB | 33/60 kB
Progress (4): 22 kB | 38 kB | 49/58 kB | 37/60 kB
Progress (5): 22 kB | 38 kB | 49/58 kB | 37/60 kB | 2.2/184 kB
Progress (5): 22 kB | 38 kB | 53/58 kB | 37/60 kB | 2.2/184 kB
Progress (5): 22 kB | 38 kB | 53/58 kB | 41/60 kB | 2.2/184 kB
Progress (5): 22 kB | 38 kB | 53/58 kB | 41/60 kB | 5.0/184 kB
Progress (5): 22 kB | 38 kB | 57/58 kB | 41/60 kB | 5.0/184 kB
Progress (5): 22 kB | 38 kB | 58 kB | 41/60 kB | 5.0/184 kB   
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 5.0/184 kB
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 7.7/184 kB
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 10/184 kB 
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 13/184 kB
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 16/184 kB
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 19/184 kB
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 21/184 kB
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 24/184 kB
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 27/184 kB
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 30/184 kB
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 32/184 kB
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 36/184 kB
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 40/184 kB
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 45/184 kB
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 49/184 kB
Progress (5): 22 kB | 38 kB | 58 kB | 45/60 kB | 53/184 kB
                                                          
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-archiver/2.5/maven-archiver-2.5.jar (22 kB at 519 kB/s)
Downloaded from central: https://repo.maven.apache.org/maven2/classworlds/classworlds/1.1-alpha-2/classworlds-1.1-alpha-2.jar (38 kB at 695 kB/s)
Progress (3): 58 kB | 49/60 kB | 53/184 kB
Progress (3): 58 kB | 53/60 kB | 53/184 kB
Progress (3): 58 kB | 57/60 kB | 53/184 kB
Progress (3): 58 kB | 60 kB | 53/184 kB   
Progress (3): 58 kB | 60 kB | 57/184 kB
                                       
Downloading from central: https://repo.maven.apache.org/maven2/commons-lang/commons-lang/2.1/commons-lang-2.1.jar
Progress (3): 58 kB | 60 kB | 61/184 kB
Progress (3): 58 kB | 60 kB | 65/184 kB
Progress (3): 58 kB | 60 kB | 69/184 kB
Progress (3): 58 kB | 60 kB | 73/184 kB
Progress (3): 58 kB | 60 kB | 77/184 kB
Progress (3): 58 kB | 60 kB | 81/184 kB
Progress (3): 58 kB | 60 kB | 85/184 kB
Progress (3): 58 kB | 60 kB | 90/184 kB
Progress (3): 58 kB | 60 kB | 94/184 kB
Progress (3): 58 kB | 60 kB | 98/184 kB
                                       
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-io/2.0.2/plexus-io-2.0.2.jar (58 kB at 1.3 MB/s)
Progress (2): 60 kB | 102/184 kB
Progress (2): 60 kB | 106/184 kB
Progress (2): 60 kB | 110/184 kB
Progress (2): 60 kB | 114/184 kB
Progress (2): 60 kB | 118/184 kB
Progress (2): 60 kB | 122/184 kB
Progress (2): 60 kB | 126/184 kB
Progress (2): 60 kB | 131/184 kB
Progress (2): 60 kB | 135/184 kB
Progress (2): 60 kB | 139/184 kB
Progress (2): 60 kB | 143/184 kB
Progress (2): 60 kB | 147/184 kB
                                
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interpolation/1.15/plexus-interpolation-1.15.jar (60 kB at 1.1 MB/s)
Progress (1): 151/184 kB
Progress (1): 155/184 kB
Progress (1): 159/184 kB
Progress (1): 163/184 kB
Progress (2): 163/184 kB | 2.2/208 kB
Progress (2): 163/184 kB | 5.0/208 kB
Progress (2): 163/184 kB | 7.7/208 kB
Progress (2): 163/184 kB | 10/208 kB 
Progress (2): 163/184 kB | 13/208 kB
Progress (2): 167/184 kB | 13/208 kB
Progress (2): 167/184 kB | 16/208 kB
Progress (2): 167/184 kB | 19/208 kB
Progress (2): 167/184 kB | 21/208 kB
Progress (2): 167/184 kB | 24/208 kB
Progress (2): 167/184 kB | 27/208 kB
Progress (2): 167/184 kB | 30/208 kB
Progress (2): 167/184 kB | 32/208 kB
Progress (2): 171/184 kB | 32/208 kB
Progress (2): 171/184 kB | 36/208 kB
Progress (2): 176/184 kB | 36/208 kB
Progress (2): 176/184 kB | 40/208 kB
Progress (2): 180/184 kB | 40/208 kB
Progress (2): 180/184 kB | 45/208 kB
Progress (2): 180/184 kB | 49/208 kB
Progress (2): 184/184 kB | 49/208 kB
Progress (2): 184 kB | 49/208 kB    
Progress (2): 184 kB | 53/208 kB
Progress (2): 184 kB | 57/208 kB
Progress (2): 184 kB | 61/208 kB
Progress (2): 184 kB | 65/208 kB
Progress (2): 184 kB | 69/208 kB
Progress (2): 184 kB | 73/208 kB
Progress (2): 184 kB | 77/208 kB
Progress (2): 184 kB | 81/208 kB
Progress (2): 184 kB | 85/208 kB
Progress (2): 184 kB | 90/208 kB
Progress (2): 184 kB | 94/208 kB
Progress (2): 184 kB | 98/208 kB
Progress (2): 184 kB | 102/208 kB
Progress (2): 184 kB | 106/208 kB
Progress (2): 184 kB | 110/208 kB
Progress (2): 184 kB | 114/208 kB
Progress (2): 184 kB | 118/208 kB
Progress (2): 184 kB | 122/208 kB
Progress (2): 184 kB | 126/208 kB
Progress (2): 184 kB | 131/208 kB
Progress (2): 184 kB | 135/208 kB
Progress (2): 184 kB | 139/208 kB
Progress (2): 184 kB | 143/208 kB
Progress (2): 184 kB | 147/208 kB
Progress (2): 184 kB | 151/208 kB
Progress (2): 184 kB | 155/208 kB
Progress (2): 184 kB | 159/208 kB
Progress (2): 184 kB | 163/208 kB
Progress (2): 184 kB | 167/208 kB
Progress (2): 184 kB | 171/208 kB
Progress (2): 184 kB | 176/208 kB
Progress (2): 184 kB | 180/208 kB
                                 
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-archiver/2.1/plexus-archiver-2.1.jar (184 kB at 2.1 MB/s)
Progress (1): 184/208 kB
Progress (1): 188/208 kB
Progress (1): 192/208 kB
Progress (1): 196/208 kB
Progress (1): 200/208 kB
Progress (1): 204/208 kB
Progress (1): 208 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/commons-lang/commons-lang/2.1/commons-lang-2.1.jar (208 kB at 2.0 MB/s)
[INFO] Building jar: /home/jenkins/workspace/First_Pipeline/server/target/server.jar
[INFO] 
[INFO] ------------------< com.example.maven-project:webapp >------------------
[INFO] Building Webapp 1.0-SNAPSHOT                                       [3/3]
[INFO] --------------------------------[ war ]---------------------------------
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-war-plugin/2.2/maven-war-plugin-2.2.pom
Progress (1): 2.8/6.5 kB
Progress (1): 5.5/6.5 kB
Progress (1): 6.5 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-war-plugin/2.2/maven-war-plugin-2.2.pom (6.5 kB at 231 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-war-plugin/2.2/maven-war-plugin-2.2.jar
Progress (1): 2.8/79 kB
Progress (1): 5.5/79 kB
Progress (1): 8.3/79 kB
Progress (1): 11/79 kB 
Progress (1): 14/79 kB
Progress (1): 16/79 kB
Progress (1): 19/79 kB
Progress (1): 22/79 kB
Progress (1): 25/79 kB
Progress (1): 27/79 kB
Progress (1): 30/79 kB
Progress (1): 33/79 kB
Progress (1): 37/79 kB
Progress (1): 41/79 kB
Progress (1): 45/79 kB
Progress (1): 49/79 kB
Progress (1): 53/79 kB
Progress (1): 57/79 kB
Progress (1): 61/79 kB
Progress (1): 66/79 kB
Progress (1): 70/79 kB
Progress (1): 74/79 kB
Progress (1): 78/79 kB
Progress (1): 79 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/plugins/maven-war-plugin/2.2/maven-war-plugin-2.2.jar (79 kB at 1.9 MB/s)
Downloading from central: https://repo.maven.apache.org/maven2/javax/servlet/servlet-api/2.5/servlet-api-2.5.pom
Progress (1): 157 B
                   
Downloaded from central: https://repo.maven.apache.org/maven2/javax/servlet/servlet-api/2.5/servlet-api-2.5.pom (157 B at 5.4 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/javax/servlet/jsp/jsp-api/2.2/jsp-api-2.2.pom
Progress (1): 2.2/5.3 kB
Progress (1): 5.0/5.3 kB
Progress (1): 5.3 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/javax/servlet/jsp/jsp-api/2.2/jsp-api-2.2.pom (5.3 kB at 184 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/glassfish/web/jsp/2.2/jsp-2.2.pom
Progress (1): 2.2/6.9 kB
Progress (1): 5.0/6.9 kB
Progress (1): 6.9 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/glassfish/web/jsp/2.2/jsp-2.2.pom (6.9 kB at 182 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/javax/servlet/servlet-api/2.5/servlet-api-2.5.jar
Downloading from central: https://repo.maven.apache.org/maven2/javax/servlet/jsp/jsp-api/2.2/jsp-api-2.2.jar
Progress (1): 2.2/50 kB
Progress (1): 5.0/50 kB
Progress (1): 7.7/50 kB
Progress (1): 10/50 kB 
Progress (1): 13/50 kB
Progress (1): 16/50 kB
Progress (1): 19/50 kB
Progress (1): 21/50 kB
Progress (1): 24/50 kB
Progress (1): 27/50 kB
Progress (1): 30/50 kB
Progress (1): 32/50 kB
Progress (1): 36/50 kB
Progress (1): 40/50 kB
Progress (1): 45/50 kB
Progress (1): 49/50 kB
Progress (1): 50 kB   
Progress (2): 50 kB | 4.1/105 kB
Progress (2): 50 kB | 7.7/105 kB
Progress (2): 50 kB | 12/105 kB 
Progress (2): 50 kB | 16/105 kB
Progress (2): 50 kB | 20/105 kB
Progress (2): 50 kB | 24/105 kB
Progress (2): 50 kB | 28/105 kB
Progress (2): 50 kB | 32/105 kB
Progress (2): 50 kB | 36/105 kB
Progress (2): 50 kB | 40/105 kB
Progress (2): 50 kB | 45/105 kB
Progress (2): 50 kB | 49/105 kB
Progress (2): 50 kB | 53/105 kB
Progress (2): 50 kB | 57/105 kB
Progress (2): 50 kB | 61/105 kB
Progress (2): 50 kB | 65/105 kB
Progress (2): 50 kB | 69/105 kB
Progress (2): 50 kB | 73/105 kB
Progress (2): 50 kB | 77/105 kB
Progress (2): 50 kB | 81/105 kB
Progress (2): 50 kB | 85/105 kB
Progress (2): 50 kB | 90/105 kB
Progress (2): 50 kB | 94/105 kB
Progress (2): 50 kB | 98/105 kB
Progress (2): 50 kB | 102/105 kB
Progress (2): 50 kB | 105 kB    
                            
Downloaded from central: https://repo.maven.apache.org/maven2/javax/servlet/jsp/jsp-api/2.2/jsp-api-2.2.jar (50 kB at 1.3 MB/s)
Downloaded from central: https://repo.maven.apache.org/maven2/javax/servlet/servlet-api/2.5/servlet-api-2.5.jar (105 kB at 2.0 MB/s)
[INFO] 
[INFO] --- maven-clean-plugin:2.5:clean (default-clean) @ webapp ---
[INFO] 
[INFO] --- maven-resources-plugin:2.5:resources (default-resources) @ webapp ---
[debug] execute contextualize
[INFO] Using 'utf-8' encoding to copy filtered resources.
[INFO] skip non existing resourceDirectory /home/jenkins/workspace/First_Pipeline/webapp/src/main/resources
[INFO] 
[INFO] --- maven-compiler-plugin:2.3.2:compile (default-compile) @ webapp ---
[INFO] No sources to compile
[INFO] 
[INFO] --- maven-resources-plugin:2.5:testResources (default-testResources) @ webapp ---
[debug] execute contextualize
[INFO] Using 'utf-8' encoding to copy filtered resources.
[INFO] skip non existing resourceDirectory /home/jenkins/workspace/First_Pipeline/webapp/src/test/resources
[INFO] 
[INFO] --- maven-compiler-plugin:2.3.2:testCompile (default-testCompile) @ webapp ---
[INFO] No sources to compile
[INFO] 
[INFO] --- maven-surefire-plugin:2.11:test (default-test) @ webapp ---
[INFO] No tests to run.
[INFO] Surefire report directory: /home/jenkins/workspace/First_Pipeline/webapp/target/surefire-reports
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-junit3/2.11/surefire-junit3-2.11.pom
Progress (1): 1.7 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-junit3/2.11/surefire-junit3-2.11.pom (1.7 kB at 57 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-junit3/2.11/surefire-junit3-2.11.jar
Progress (1): 2.2/26 kB
Progress (1): 5.0/26 kB
Progress (1): 7.7/26 kB
Progress (1): 10/26 kB 
Progress (1): 13/26 kB
Progress (1): 16/26 kB
Progress (1): 19/26 kB
Progress (1): 21/26 kB
Progress (1): 24/26 kB
Progress (1): 26 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/surefire/surefire-junit3/2.11/surefire-junit3-2.11.jar (26 kB at 878 kB/s)

-------------------------------------------------------
 T E S T S
-------------------------------------------------------

Results :

Tests run: 0, Failures: 0, Errors: 0, Skipped: 0

[INFO] 
[INFO] --- maven-war-plugin:2.2:war (default-war) @ webapp ---
Downloading from central: https://repo.maven.apache.org/maven2/com/thoughtworks/xstream/xstream/1.3.1/xstream-1.3.1.pom
Progress (1): 2.2/11 kB
Progress (1): 5.0/11 kB
Progress (1): 7.8/11 kB
Progress (1): 11/11 kB 
Progress (1): 11 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/com/thoughtworks/xstream/xstream/1.3.1/xstream-1.3.1.pom (11 kB at 369 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/com/thoughtworks/xstream/xstream-parent/1.3.1/xstream-parent-1.3.1.pom
Progress (1): 2.8/14 kB
Progress (1): 5.5/14 kB
Progress (1): 8.3/14 kB
Progress (1): 11/14 kB 
Progress (1): 14/14 kB
Progress (1): 14 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/com/thoughtworks/xstream/xstream-parent/1.3.1/xstream-parent-1.3.1.pom (14 kB at 518 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/xpp3/xpp3_min/1.1.4c/xpp3_min-1.1.4c.pom
Progress (1): 1.6 kB
                    
Downloaded from central: https://repo.maven.apache.org/maven2/xpp3/xpp3_min/1.1.4c/xpp3_min-1.1.4c.pom (1.6 kB at 56 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-filtering/1.0-beta-2/maven-filtering-1.0-beta-2.pom
Progress (1): 2.8/4.0 kB
Progress (1): 4.0 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-filtering/1.0-beta-2/maven-filtering-1.0-beta-2.pom (4.0 kB at 168 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-shared-components/10/maven-shared-components-10.pom
Progress (1): 2.2/8.4 kB
Progress (1): 5.0/8.4 kB
Progress (1): 7.8/8.4 kB
Progress (1): 8.4 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-shared-components/10/maven-shared-components-10.pom (8.4 kB at 351 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/9/maven-parent-9.pom
Progress (1): 2.2/33 kB
Progress (1): 5.0/33 kB
Progress (1): 7.8/33 kB
Progress (1): 11/33 kB 
Progress (1): 13/33 kB
Progress (1): 16/33 kB
Progress (1): 19/33 kB
Progress (1): 21/33 kB
Progress (1): 24/33 kB
Progress (1): 27/33 kB
Progress (1): 30/33 kB
Progress (1): 32/33 kB
Progress (1): 33 kB   
                   
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/maven-parent/9/maven-parent-9.pom (33 kB at 1.1 MB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/1.5.6/plexus-utils-1.5.6.pom
Progress (1): 2.2/5.3 kB
Progress (1): 5.0/5.3 kB
Progress (1): 5.3 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-utils/1.5.6/plexus-utils-1.5.6.pom (5.3 kB at 252 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/1.0.12/plexus-1.0.12.pom
Progress (1): 2.2/9.8 kB
Progress (1): 5.0/9.8 kB
Progress (1): 7.8/9.8 kB
Progress (1): 9.8 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus/1.0.12/plexus-1.0.12.pom (9.8 kB at 363 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interpolation/1.6/plexus-interpolation-1.6.pom
Progress (1): 1.4/2.9 kB
Progress (1): 2.9 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/org/codehaus/plexus/plexus-interpolation/1.6/plexus-interpolation-1.6.pom (2.9 kB at 126 kB/s)
Downloading from central: https://repo.maven.apache.org/maven2/com/thoughtworks/xstream/xstream/1.3.1/xstream-1.3.1.jar
Downloading from central: https://repo.maven.apache.org/maven2/xpp3/xpp3_min/1.1.4c/xpp3_min-1.1.4c.jar
Downloading from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-filtering/1.0-beta-2/maven-filtering-1.0-beta-2.jar
Progress (1): 2.2/431 kB
Progress (1): 5.0/431 kB
Progress (1): 7.7/431 kB
Progress (1): 10/431 kB 
Progress (1): 13/431 kB
Progress (1): 16/431 kB
Progress (1): 19/431 kB
Progress (2): 19/431 kB | 2.2/33 kB
Progress (2): 21/431 kB | 2.2/33 kB
Progress (2): 21/431 kB | 5.0/33 kB
Progress (2): 24/431 kB | 5.0/33 kB
Progress (2): 27/431 kB | 5.0/33 kB
Progress (2): 27/431 kB | 7.7/33 kB
Progress (2): 30/431 kB | 7.7/33 kB
Progress (2): 30/431 kB | 10/33 kB 
Progress (2): 32/431 kB | 10/33 kB
Progress (2): 32/431 kB | 13/33 kB
Progress (2): 32/431 kB | 16/33 kB
Progress (2): 32/431 kB | 19/33 kB
Progress (2): 36/431 kB | 19/33 kB
Progress (2): 36/431 kB | 21/33 kB
Progress (2): 40/431 kB | 21/33 kB
Progress (2): 45/431 kB | 21/33 kB
Progress (2): 45/431 kB | 24/33 kB
Progress (2): 49/431 kB | 24/33 kB
Progress (2): 49/431 kB | 27/33 kB
Progress (2): 49/431 kB | 30/33 kB
Progress (2): 49/431 kB | 32/33 kB
Progress (2): 53/431 kB | 32/33 kB
Progress (2): 53/431 kB | 33 kB   
Progress (2): 57/431 kB | 33 kB
Progress (2): 61/431 kB | 33 kB
Progress (2): 65/431 kB | 33 kB
Progress (2): 69/431 kB | 33 kB
Progress (2): 73/431 kB | 33 kB
Progress (2): 77/431 kB | 33 kB
Progress (2): 81/431 kB | 33 kB
Progress (2): 85/431 kB | 33 kB
Progress (2): 90/431 kB | 33 kB
Progress (2): 94/431 kB | 33 kB
Progress (2): 98/431 kB | 33 kB
Progress (2): 102/431 kB | 33 kB
Progress (2): 106/431 kB | 33 kB
Progress (2): 110/431 kB | 33 kB
Progress (2): 114/431 kB | 33 kB
Progress (2): 118/431 kB | 33 kB
Progress (2): 122/431 kB | 33 kB
Progress (2): 126/431 kB | 33 kB
Progress (2): 131/431 kB | 33 kB
Progress (2): 135/431 kB | 33 kB
Progress (2): 139/431 kB | 33 kB
Progress (2): 143/431 kB | 33 kB
Progress (2): 147/431 kB | 33 kB
Progress (2): 151/431 kB | 33 kB
Progress (2): 155/431 kB | 33 kB
Progress (2): 159/431 kB | 33 kB
Progress (2): 163/431 kB | 33 kB
Progress (2): 167/431 kB | 33 kB
Progress (2): 171/431 kB | 33 kB
Progress (2): 176/431 kB | 33 kB
Progress (2): 180/431 kB | 33 kB
Progress (2): 184/431 kB | 33 kB
Progress (2): 188/431 kB | 33 kB
Progress (2): 192/431 kB | 33 kB
Progress (2): 196/431 kB | 33 kB
Progress (2): 200/431 kB | 33 kB
Progress (2): 204/431 kB | 33 kB
Progress (2): 208/431 kB | 33 kB
Progress (2): 212/431 kB | 33 kB
Progress (2): 217/431 kB | 33 kB
Progress (2): 221/431 kB | 33 kB
Progress (2): 225/431 kB | 33 kB
Progress (2): 229/431 kB | 33 kB
Progress (2): 233/431 kB | 33 kB
Progress (3): 233/431 kB | 33 kB | 4.1/25 kB
Progress (3): 233/431 kB | 33 kB | 7.6/25 kB
Progress (3): 233/431 kB | 33 kB | 12/25 kB 
Progress (3): 233/431 kB | 33 kB | 16/25 kB
Progress (3): 233/431 kB | 33 kB | 20/25 kB
Progress (3): 233/431 kB | 33 kB | 24/25 kB
Progress (3): 233/431 kB | 33 kB | 25 kB   
Progress (3): 237/431 kB | 33 kB | 25 kB
                                        
Downloaded from central: https://repo.maven.apache.org/maven2/org/apache/maven/shared/maven-filtering/1.0-beta-2/maven-filtering-1.0-beta-2.jar (33 kB at 651 kB/s)
Progress (2): 241/431 kB | 25 kB
Progress (2): 245/431 kB | 25 kB
Progress (2): 249/431 kB | 25 kB
Progress (2): 253/431 kB | 25 kB
Progress (2): 258/431 kB | 25 kB
Progress (2): 262/431 kB | 25 kB
Progress (2): 266/431 kB | 25 kB
Progress (2): 270/431 kB | 25 kB
Progress (2): 274/431 kB | 25 kB
Progress (2): 278/431 kB | 25 kB
Progress (2): 282/431 kB | 25 kB
Progress (2): 286/431 kB | 25 kB
Progress (2): 290/431 kB | 25 kB
Progress (2): 294/431 kB | 25 kB
Progress (2): 298/431 kB | 25 kB
Progress (2): 303/431 kB | 25 kB
Progress (2): 307/431 kB | 25 kB
Progress (2): 311/431 kB | 25 kB
Progress (2): 315/431 kB | 25 kB
Progress (2): 319/431 kB | 25 kB
Progress (2): 323/431 kB | 25 kB
Progress (2): 327/431 kB | 25 kB
Progress (2): 331/431 kB | 25 kB
Progress (2): 335/431 kB | 25 kB
Progress (2): 339/431 kB | 25 kB
Progress (2): 344/431 kB | 25 kB
Progress (2): 348/431 kB | 25 kB
Progress (2): 352/431 kB | 25 kB
Progress (2): 356/431 kB | 25 kB
Progress (2): 360/431 kB | 25 kB
Progress (2): 364/431 kB | 25 kB
Progress (2): 368/431 kB | 25 kB
                                
Downloaded from central: https://repo.maven.apache.org/maven2/xpp3/xpp3_min/1.1.4c/xpp3_min-1.1.4c.jar (25 kB at 342 kB/s)
Progress (1): 372/431 kB
Progress (1): 376/431 kB
Progress (1): 380/431 kB
Progress (1): 384/431 kB
Progress (1): 389/431 kB
Progress (1): 393/431 kB
Progress (1): 397/431 kB
Progress (1): 401/431 kB
Progress (1): 405/431 kB
Progress (1): 409/431 kB
Progress (1): 413/431 kB
Progress (1): 417/431 kB
Progress (1): 421/431 kB
Progress (1): 425/431 kB
Progress (1): 430/431 kB
Progress (1): 431 kB    
                    
Downloaded from central: https://repo.maven.apache.org/maven2/com/thoughtworks/xstream/xstream/1.3.1/xstream-1.3.1.jar (431 kB at 4.1 MB/s)
[INFO] Packaging webapp
[INFO] Assembling webapp [webapp] in [/home/jenkins/workspace/First_Pipeline/webapp/target/webapp]
[INFO] Processing war project
[INFO] Copying webapp resources [/home/jenkins/workspace/First_Pipeline/webapp/src/main/webapp]
[INFO] Webapp assembled in [64 msecs]
[INFO] Building war: /home/jenkins/workspace/First_Pipeline/webapp/target/webapp.war
[INFO] WEB-INF/web.xml already added, skipping
[INFO] ------------------------------------------------------------------------
[INFO] Reactor Summary:
[INFO] 
[INFO] Maven Project 1.0-SNAPSHOT ......................... SUCCESS [  1.954 s]
[INFO] Server ............................................. SUCCESS [  7.890 s]
[INFO] Webapp 1.0-SNAPSHOT ................................ SUCCESS [  1.530 s]
[INFO] ------------------------------------------------------------------------
[INFO] BUILD SUCCESS
[INFO] ------------------------------------------------------------------------
[INFO] Total time: 11.592 s
[INFO] Finished at: 2019-03-01T14:00:42Z
[INFO] ------------------------------------------------------------------------
[Pipeline] }
[Pipeline] // container
[Pipeline] }
[Pipeline] // withEnv
[Pipeline] }
[Pipeline] // stage
[Pipeline] stage
[Pipeline] { (Docker Build)
[Pipeline] tool
[Pipeline] envVarsForTool
[Pipeline] withEnv
[Pipeline] {
[Pipeline] container
[Pipeline] {
[Pipeline] sh
+ docker build . -t ee-dtr.sttproductions.de/sttproductions/webapp:k8s-23
Sending build context to Docker daemon  247.8kB

Step 1/4 : FROM tomcat:9-jre11-slim
9-jre11-slim: Pulling from library/tomcat
6ae821421a7d: Already exists
187061ad2a29: Already exists
c3e07dee1e7e: Already exists
9703bd99cc4e: Already exists
9f0990bb7f89: Already exists
b8143d8fede8: Already exists
ff56d95f6403: Already exists
7d103fbb1b98: Already exists
96e2fb986c1f: Already exists
81c63a9573f9: Already exists
Digest: sha256:fa9bd738ec2cc4eead02a5f81c69788d53667f320299c7ff8e84749f668ee530
Status: Downloaded newer image for tomcat:9-jre11-slim
 ---> 04b6cfe4c5a2
Step 2/4 : ADD ./webapp/target/*.war /usr/local/tomcat/webapps/
 ---> ac31d3d3208a
Step 3/4 : EXPOSE 8080
 ---> Running in 0b6e6df8da84
Removing intermediate container 0b6e6df8da84
 ---> 901bcdffa3ba
Step 4/4 : CMD ["catalina.sh", "run"]
 ---> Running in 4116f019f7ca
Removing intermediate container 4116f019f7ca
 ---> f50f93fa0991
Successfully built f50f93fa0991
Successfully tagged ee-dtr.sttproductions.de/sttproductions/webapp:k8s-23
+ docker login -u devjenkins -p jenkins ee-dtr.sttproductions.de
WARNING! Using --password via the CLI is insecure. Use --password-stdin.
WARNING! Your password will be stored unencrypted in /home/jenkins/.docker/config.json.
Configure a credential helper to remove this warning. See
https://docs.docker.com/engine/reference/commandline/login/#credentials-store

Login Succeeded
+ docker image push ee-dtr.sttproductions.de/sttproductions/webapp:k8s-23
The push refers to repository [ee-dtr.sttproductions.de/sttproductions/webapp]
7668dd1b350a: Preparing
a1b563f52754: Preparing
873796cf443d: Preparing
e079ac34c798: Preparing
a49b7c884726: Preparing
8818ec36ec5d: Preparing
2d41ac9eef46: Preparing
d9bd08c96260: Preparing
a52e32680bb2: Preparing
85b294f018d5: Preparing
0a07e81f5da3: Preparing
2d41ac9eef46: Waiting
d9bd08c96260: Waiting
8818ec36ec5d: Waiting
85b294f018d5: Waiting
0a07e81f5da3: Waiting
a52e32680bb2: Waiting
a49b7c884726: Layer already exists
e079ac34c798: Layer already exists
873796cf443d: Layer already exists
a1b563f52754: Layer already exists
8818ec36ec5d: Layer already exists
2d41ac9eef46: Layer already exists
a52e32680bb2: Layer already exists
d9bd08c96260: Layer already exists
85b294f018d5: Layer already exists
0a07e81f5da3: Layer already exists
7668dd1b350a: Pushed
k8s-23: digest: sha256:cc1718b506956c39f4556dd983fe79b91d724fc9286edf57e60dba490255a18c size: 2617
[Pipeline] }
[Pipeline] // container
[Pipeline] }
[Pipeline] // withEnv
[Pipeline] }
[Pipeline] // stage
[Pipeline] }
[Pipeline] // withEnv
[Pipeline] }
[Pipeline] // withEnv
[Pipeline] }
[Pipeline] // node
[Pipeline] End of Pipeline
Finished: SUCCESS
```

## Part 5 - Check your image build

You should be able to run now `docker container run --rm -p 8080:8080 YOURDTRURL/REPO/webapp:k8s-BUILDNUMBER`

When browsing to your docker node URL `http://dockernodeurl:8080/webapp` you should be greeted by welcome message:

![part03-k8sjenkins08](../images/part03-k8sjenkins08.png)/

## Conclusion

Kubernetes and Jenkins provide a powerful basis for a high scalable CI/CD platform. There are many different ways to realize a CI/CD Kubernetes based setup. In our example, Jenkins uses 2 different containers to build a maven based tomcat image. The first container will take care of the maven build, while the second container will take care of any Docker tasks. 

The current setup is meant only for training purposes.


