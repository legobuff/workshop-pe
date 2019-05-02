# Configure Docker to use the recommended OS storage/graph driver

By the end of this exercise, you should be able to:

 - Reconfigure the Docker Storage Engine to use the Docker recommended OS graph driver
 
## Part 1 - Introduction

Every Linux distribution has their own preferred storage or graph driver recommendation. We highly recommend to follow the configuration guidance to be able to use the full performance potential of your installation.

## Part 2 - Check the current setup of your Docker Engine

Following official Docker Installation instructions, your installation will most likely be by default set up with the local `devicemapper driver`. You can check the current state of your installation by running `docker info`. The output will contain the following information:

    ```
    Storage Driver: devicemapper
     Pool Name: docker-253:1-50336964-pool
     Pool Blocksize: 65.54kB
     Base Device Size: 10.74GB
     Backing Filesystem: xfs
     Udev Sync Supported: true
     Data file: /dev/loop0
     Metadata file: /dev/loop1
     Data loop file: /var/lib/docker/devicemapper/devicemapper/data
     Metadata loop file: /var/lib/docker/devicemapper/devicemapper/metadata
     Data Space Used: 17.35GB
     Data Space Total: 107.4GB
     Data Space Available: 4.35GB
     Metadata Space Used: 27.34MB
     Metadata Space Total: 2.147GB
     Metadata Space Available: 2.12GB
     Thin Pool Minimum Free Space: 10.74GB
     Deferred Removal Enabled: true
     Deferred Deletion Enabled: true
     Deferred Deleted Device Count: 0
     Library Version: 1.02.149-RHEL7 (2018-07-20)
    ```

an `overlay2` can look like this output:

    ```
    Storage Driver: overlay2
     Backing Filesystem: xfs
     Supports d_type: true
     Native Overlay Diff: true
    Logging Driver: json-file
    Cgroup Driver: cgroupfs
    ```

## Part 3 - OS storage driver overview.

### Ubunutu

Supported drivers: overlay2, aufs

Alternative drivers: overlay, devicemapper, zfs, vfs

### Debian

Supported drivers: overlay2, aufs, devicemapper

Alternative drivers: overlay, vfs

### CentOS/Fedora

Supported drivers: overlay2

Alternative drivers: overlay, devicemapper, zfs, vfs

### SUSE SLES

Supported drivers: btrfs

Alternative drivers: devicemapper

## Part 4 - Re-configure the Docker Engine.

**Important Note**
Make sure you change the storage/graph driver of a fresh Docker Engine installation or make sure all your images,volumes and containers are moved to another node or repository. All local images, volumes and containers will be unusable by changing the storage/graph drivers. 

### overlay2

1.  Make sure your system fully supports `overlay2`. Usually Linux OS with a kernel of 3.18 and later fully support overlay2

2.  Provide a XFS formated storage. The size of the storage depends on your image usage of the local system

3.  Stop the docker service `sudo systemctl stop docker`

4.  Backup your current `/var/lib/docker` folder `cp -au /var/lib/docker /var/lib/docker.bk`

5.  Mount the XFS storage drive to `/var/lib/docker` make sure you make it constant by providing a fstab entry of your Linux OS

6.  Edit your `/etc/docker/daemon.json` file to contain: 

    ```
    {
      "storage-driver": "overlay2"
    }
    ```

7.  Restart docker by `sudo systemctl start docker`

8.  Check with `docker info` the current storage driver usage. It should read:
    ```
    Containers: 0
    Images: 0
    Storage Driver: overlay2
     Backing Filesystem: xfs
     Supports d_type: true
     Native Overlay Diff: true
     ```

### devicemapper
1.  Make sure your system fully supports `devicemapper`. Usually Linux OS with a kernel of 3.18 and later fully support devicemapper, but you might need to install `lvm` to fully support it.

2.  Provide an unformatted storage. The size of the storage depends on your image usage of the local system. `devicemapper` drivers will take care of the formating.

3.  Stop the docker service `sudo systemctl stop docker`

4.  Edit your `/etc/docker/daemon.json` file to contain: 

    ```
    {
      "storage-driver": "devicemapper"
    }
    ```

5.  Restart docker by `sudo systemctl start docker`

6. Check with `docker info` the current storage driver usage. It should read:
    ```
    Containers: 0
        Running: 0
        Paused: 0
        Stopped: 0
      Images: 0
      Server Version: 17.03.1-ce
      Storage Driver: devicemapper
      Pool Name: docker-202:1-8413957-pool
      Pool Blocksize: 65.54 kB
      Base Device Size: 10.74 GB
      Backing Filesystem: xfs
      Data file: /dev/loop0
      Metadata file: /dev/loop1
      Data Space Used: 11.8 MB
      Data Space Total: 107.4 GB
      Data Space Available: 7.44 GB
      Metadata Space Used: 581.6 KB
      Metadata Space Total: 2.147 GB
      Metadata Space Available: 2.147 GB
      Thin Pool Minimum Free Space: 10.74 GB
      Udev Sync Supported: true
      Deferred Removal Enabled: false
      Deferred Deletion Enabled: false
      Deferred Deleted Device Count: 0
      Data loop file: /var/lib/docker/devicemapper/data
      Metadata loop file: /var/lib/docker/devicemapper/metadata
      Library Version: 1.02.135-RHEL7 (2016-11-16)
     ```

### btrfs
1.  Make sure your system fully supports `btrfs`. You can do this by running: `sudo cat /proc/filesystems | grep btrfs`

2.  Provide an btrfs formated storage. The size of the storage depends on your image usage of the local system.

3.  Stop the docker service `sudo systemctl stop docker`

4.  Backup your current `/var/lib/docker` folder `cp -au /var/lib/docker /var/lib/docker.bk`

5.  Mount the btrfs storage drive to `/var/lib/docker` make sure you make it constant by providing a fstab entry of your Linux OS

6.  Edit your `/etc/docker/daemon.json` file to contain: 

    ```
    {
      "storage-driver": "btrfs"
    }
    ```

7.  Restart docker by `sudo systemctl start docker`

8.  Check with `docker info` the current storage driver usage. It should read:

    ```
    Containers: 0
     Running: 0
     Paused: 0
     Stopped: 0
    Images: 0
    Server Version: 17.03.1-ce
    Storage Driver: btrfs
     Build Version: Btrfs v4.4
     Library Version: 101
    <output truncated>
     ```


## Conclusion

The performance of Docker is highly depending on the provisioned storage and it's access. Please make sure you follow Docker's recommendations on which storage driver to use within production grade environments.

Further reading: 

- https://docs.docker.com/storage/storagedriver/select-storage-driver/
- https://docs.docker.com/storage/storagedriver/overlayfs-driver/
- https://docs.docker.com/storage/storagedriver/btrfs-driver/