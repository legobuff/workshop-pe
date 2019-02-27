# Create a simple RBAC structure

By the end of this exercise, you should be able to:

 - Create a simple RBAC structure within UCP
 - Understand the relations between Organisations, Teams and Users

## Part 1 - Organizations

In each part we will create one piece of the RBAC structure. We will start with **Organizations**

1. Log into your UCP installation with an admin user, e.g. `admin`

![rbac01](../images/rbac01.png)/


2. Select your `Access Control` and `Orgs & Teams`

![rbac02](../images/rbac02.png)/


3. Select the `Create`Button and create the following Teams:

- developers

- devops

The result should reflect this:
![rbac03](../images/rbac03.png)/



## Part 2 - Teams

In this part we will create the **Teams**

1. Still in `Orgs & Teams` select the Organization `developers`

2. Click the **+** in the upper right corner to add a new team and call it `alpha`. Click `Create`to complete the first team.

3. Repeat the steps with a second team called `beta`

The result should reflect this:
![rbac04](../images/rbac04.png)/

4. Repeat step 2. and 3. with the Organization `devops`. The teams should be named `members` and `sensitive`

The result should reflect this:
![rbac05](../images/rbac05.png)/



## Part 3 - Users

In this part we will create the **Users** and assign them to their **Teams**

1. Select your `Access Control` and `Users`

2. Select the `Create`Button and create the following Users:

- alice
- billy
- chuck
- dave
- earl
- frank
- gertrude
- leonard

The result should reflect this:
![rbac06](../images/rbac06.png)/

3. Switch to `Orgs & Teams` and select the Organization `developers`. Assign the users to the following Teams by clicking the upper right **+**:

- alpha: alice, billy, leonard
- beta: chuck, dave, leonard

4. Switch to `Orgs & Teams` and select the Organization `devops`. Assign the users to the following Teams by clicking the upper right **+**:

- members: earl, frank, gertrude
- sensitive: gertrude


## Part 4 - Create Collections

We want our cluster to be split into three parts: `dev`, `staging`, `production`. Therefor we need to create **collections** to create a virtual possability to seperate our cluster.

1. Select your `Shared Resources` and `Collections`

2. Select `View Children` on the default collection `Swarm`

3. Select `Create Collection` and provide the name `dev`

4. Repeat step 3. for collection `staging` and `production`

The result should reflect this:
![rbac07](../images/rbac07.png)/

5. Select `View Children` on the `dev`collection.

6. Select `Create Collection` and provide the name `alpha`

7. Repeat step 5. - 6. to create the following collections:

```
dev/alpha
dev/beta
staging/alpha
staging/beta
production/alpha
production/beta
```
The result for each parent collection should reflect this:

![rbac08](../images/rbac08.png)/


## Part 5 - Role assignment

As we have a simple RBAC structure now, we need to grant the Teams their respective management rights.

1. Select your `Access Control` and `Grants`

2. Select `Swarm` at the top of the Grants page

3. Select `Create Grant` and provide the following details:
- Organizations -> Organizations: `developers` -> Team(Optional) -> `alpha` abd click `NEXT`
- Resource Set -> `View Children` on Swarm -> `View Children` on dev -> select collection `alpha`
- Role -> Select `Restricted Control` and `Create`

4. Repeat step 3. to apply the following table to your Teams and Collections:

![rbac09](../images/rbac09.png)/

5. Delete the grant `Org - docker-datacenter // Role: Scheduler`. This rule will cause anyone within the current setup to be able to schedule within the cluster. As we want restricted access to our cluster, this rule must be deleted.

The result for each parent collection should reflect this:

![rbac10](../images/rbac10.png)/




...

## Conclusion

In addition to summarizing the exercise, this is your opportunity to provide some broader context for what just happened; what are some topics for further study? Where does this fit into the bigger picture of the story we're telling in this workshop?
