const users = [{ id: 1, name: 'Ada' }];

async function listUsers() {
  return users;
}

async function findUser(id) {
  const all = await listUsers();
  return all.find((user) => user.id === id);
}

module.exports = { listUsers, findUser };
