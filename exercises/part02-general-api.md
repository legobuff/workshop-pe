# Docker Enterprise API

By the end of this exercise, you should be able to:

 - Find the API Documentation
 - Understand the possible solutions archivable with Docker Enterprise API
 
## Introduction

Many companies relay more and more on automation within their infrastructure. While Docker EE provides excellent usability, it still can be unpractible to use the WebUI for automated tasks. However

However you will be able to find the following API documentations:

### UCP
https://docs.docker.com/datacenter/ucp/2.2/reference/api/#!/Config/ConfigList

### API
https://docs.docker.com/reference/dtr/2.5/api/


## Examples

### UCP
- UCP should be load balanced through external LB solutions. The LB solution can check the health by browsing to `http://ucp-manager/_ping`, which is an API GET Request
- UCP can quickly provide information on it's up-to-date status by accessing `http://ucp-manager/info` and `http://ucp-manager/version`

### DTR
- A customer requests to build image scanning into his inhouse support or audit routine. This could be done with `imagescan` API requests
- You might want to provide DTR searches for external applictions to provide a complete overview.

## Conclusion

Docker EE's API provides all necessary access to the backend to include Docker EE in companies complete automation strategy.

Further reading:
https://docs.docker.com/datacenter/ucp/2.2/reference/api/#!/Config/ConfigList
https://docs.docker.com/reference/dtr/2.5/api/


