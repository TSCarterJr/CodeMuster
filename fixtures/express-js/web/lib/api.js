export async function loadUsers() {
  const response = await fetch('/api/users');
  return response.json();
}

export async function loadUser(id) {
  const response = await fetch(`/api/users/${id}`);
  return response.json();
}
