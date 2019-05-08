# Configure Docker Engine to use an untrusted registry

By the end of this exercise, you should be able to:

 - Make use of HTTP or untrusted registry
 

## Introduction

In general Docker highly suggests to only use trusted registries with well managed SSL certificates. It is very important to learn the usage of SSL certificates with Docker DTR. You can follow the following exercise to provide valid certificates for your environment:

- [Update UCP and DTR to use self provided SSL certificates](https://github.com/stefantrimborn/workshop-pe/blob/master/exercises/part02-general-ssl-certificates.md)

## Reconfigure Docker Engine for untrusted registries

1. Edit the `daemon.json` file, whose default location is `/etc/docker/daemon.json` on Linux or `C:\ProgramData\docker\config\daemon.json` on Windows Server. If `daemon.json` is not available you can create it.

2. Edit `daemon.json` as following:

```
{
  "insecure-registries" : ["myregistrydomain.com:5000"]
}
```

3. Restart Docker for the changes to take effect.

After the reconfiguration the engine will work as following:

```
First, try using HTTPS.
- If HTTPS is available but the certificate is invalid, ignore the error about the certificate.
- If HTTPS is not available, fall back to HTTP.
```

Even though you might want to use untrusted certificates, make sure your CA certificates are properly installed on the OS machine.

## Conclusion

Untrusted Registries are usable, but should not be used in production environments.

Further reading:
https://docs.docker.com/registry/insecure/


